using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Modeler.Core.Definition;
using Modeler.Core.Editing;
using Modeler.Core.Errors;
using CoreValues = Modeler.Core.Expressions.Values;

namespace Modeler.Civil3D.Ui;

/// <summary>Expõe os parâmetros de uma definição ao PropertyGrid: categoria = <c>grupo</c>,
/// nome exibido = <c>rotulo</c>, descrição, lista suspensa para <c>valores</c>.
/// Valores inválidos são recusados pelo próprio <see cref="ParameterDef.Coerce"/>.</summary>
internal sealed class ParameterBag : CustomTypeDescriptor
{
    public const string NameProperty = "__nome";

    public ParameterBag(DeviceDefinition definition, IReadOnlyDictionary<string, object> values, string instanceName)
    {
        Definition = definition;
        InstanceName = instanceName;
        Values = definition.Parameters.ToDictionary(
            p => p.Name,
            p => values.TryGetValue(p.Name, out var v) ? v : p.Default,
            StringComparer.Ordinal);
    }

    public DeviceDefinition Definition { get; }

    public Dictionary<string, object> Values { get; }

    public string InstanceName { get; set; }

    /// <summary>Último parâmetro editado (para <c>editado()</c>) e o que a edição mudou além dele.</summary>
    public string? LastEdited { get; private set; }

    public IReadOnlyList<string> LastMessages { get; private set; } = Array.Empty<string>();

    public override PropertyDescriptorCollection GetProperties() => GetProperties(Array.Empty<Attribute>());

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
    {
        var list = new List<PropertyDescriptor> { new NameDescriptor() };
        list.AddRange(Definition.Parameters.Select(p => (PropertyDescriptor)new ParameterDescriptor(p)));
        return new PropertyDescriptorCollection(list.ToArray());
    }

    public override object GetPropertyOwner(PropertyDescriptor? pd) => this;

    private sealed class NameDescriptor : PropertyDescriptor
    {
        public NameDescriptor()
            : base(NameProperty, new Attribute[]
            {
                new CategoryAttribute("0. Identificação"), new DisplayNameAttribute("Nome"),
                new DescriptionAttribute("Nome da instância (usado em relatórios e na exportação SWMM)."),
            })
        {
        }

        public override Type ComponentType => typeof(ParameterBag);

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(string);

        public override bool CanResetValue(object component) => false;

        public override object GetValue(object? component) => ((ParameterBag)component!).InstanceName;

        public override void ResetValue(object component)
        {
        }

        public override void SetValue(object? component, object? value)
        {
            var text = (value as string ?? "").Trim();
            if (text.Length == 0)
            {
                throw new ArgumentException("o nome não pode ficar vazio");
            }

            ((ParameterBag)component!).InstanceName = text;
        }

        public override bool ShouldSerializeValue(object component) => false;
    }

    private sealed class ParameterDescriptor : PropertyDescriptor
    {
        private readonly ParameterDef _parameter;

        public ParameterDescriptor(ParameterDef parameter)
            : base(parameter.Name, BuildAttributes(parameter))
        {
            _parameter = parameter;
        }

        public override Type ComponentType => typeof(ParameterBag);

        /// <summary>Conexões vêm dos tubos (DM_CONECTAR); aqui só são mostradas.</summary>
        public override bool IsReadOnly => _parameter.Type == ParameterType.Conexoes;

        public override Type PropertyType => _parameter.Type switch
        {
            ParameterType.Texto or ParameterType.Conexoes => typeof(string),
            ParameterType.Booleano => typeof(bool),
            _ => typeof(double),
        };

        public override TypeConverter Converter => _parameter.AllowedValues is { Count: > 0 }
            ? new ListConverter(_parameter)
            : base.Converter;

        public override bool CanResetValue(object component) => true;

        public override object GetValue(object? component)
        {
            var value = ((ParameterBag)component!).Values[_parameter.Name];
            return _parameter.Type == ParameterType.Conexoes ? DescribeConnections(value) : value;
        }

        public override void ResetValue(object component) => ((ParameterBag)component).Values[_parameter.Name] = _parameter.Default;

        public override void SetValue(object? component, object? value)
        {
            try
            {
                // como o grip: código de catálogo → "generico" e regras de ao_editar
                var bag = (ParameterBag)component!;
                var edited = DeviceEditing.Edit(bag.Definition, bag.Values, _parameter.Name, value, out var messages);
                foreach (var (name, v) in edited)
                {
                    bag.Values[name] = v;
                }

                bag.LastEdited = _parameter.Name;
                bag.LastMessages = messages;
            }
            catch (ModelerException ex)
            {
                // o PropertyGrid mostra a mensagem e mantém o valor anterior (inclui erro numa regra de ao_editar)
                throw new ArgumentException(ex.Reason, ex);
            }
        }

        public override bool ShouldSerializeValue(object component) =>
            !Equals(((ParameterBag)component).Values[_parameter.Name], _parameter.Default);

        private static Attribute[] BuildAttributes(ParameterDef p)
        {
            var unit = p.Unit.Length > 0 ? $" [{p.Unit}]" : "";
            var limits = p.Min is null && p.Max is null
                ? ""
                : $" Intervalo: {(p.Min is { } a ? CoreValues.FormatNumber(a) : "-∞")} a {(p.Max is { } b ? CoreValues.FormatNumber(b) : "+∞")}.";
            var target = p.Target == TargetKind.Nenhum ? "" : $" Pode ser obtido de: {p.Target.ToString().ToLowerInvariant()}.";
            return new Attribute[]
            {
                new CategoryAttribute(p.Group ?? "Parâmetros"),
                new DisplayNameAttribute((p.Label ?? p.Name) + unit),
                new DescriptionAttribute($"{p.Name}: {p.Description ?? p.Label ?? ""}{limits}{target}".Trim()),
            };
        }

        private static string DescribeConnections(object value) =>
            value is IReadOnlyList<object> list
                ? list.Count == 0
                    ? "(nenhuma — use DM_CONECTAR)"
                    : string.Join("; ", list.OfType<IReadOnlyDictionary<string, object>>().Select(c =>
                        $"{c["nome"]}: {CoreValues.FormatNumber((double)c["angulo"])}°, elevação {CoreValues.FormatNumber((double)c["cota"])}, Ø {CoreValues.FormatNumber((double)c["diametro"])}"))
                : CoreValues.Format(value);
    }

    /// <summary>Lista suspensa exclusiva com os valores permitidos.</summary>
    private sealed class ListConverter : TypeConverter
    {
        private readonly ParameterDef _parameter;

        public ListConverter(ParameterDef parameter)
        {
            _parameter = parameter;
        }

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
            new((ICollection)_parameter.AllowedValues!.ToList());

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            var text = (string)value;
            return _parameter.IsNumeric
                ? double.Parse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture)
                : text;
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            destinationType == typeof(string) ? CoreValues.Format(value) : base.ConvertTo(context, culture, value, destinationType);
    }
}
