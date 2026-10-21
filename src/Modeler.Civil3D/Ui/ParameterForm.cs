using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Modeler.Core.Definition;
using Modeler.Core.Errors;
using CoreValues = Modeler.Core.Expressions.Values;
using Modeler.Core.Results;

namespace Modeler.Civil3D.Ui;

/// <summary>Janela de parâmetros: lista de dispositivos do catálogo (só ao inserir), PropertyGrid
/// com os parâmetros e um painel que reavalia a cada mudança mostrando variáveis, quantitativos,
/// avisos e erros. OK só fica habilitado quando a avaliação é válida.</summary>
internal sealed class ParameterForm : Form
{
    private readonly ListBox _devices = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly PropertyGrid _grid = new() { Dock = DockStyle.Fill, ToolbarVisible = false, PropertySort = PropertySort.Categorized };
    private readonly TextBox _summary = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Font = new Font(FontFamily.GenericMonospace, 9f),
    };

    private readonly Button _ok = new() { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
    private readonly Func<DeviceDefinition, string> _nameFor;
    private ParameterBag? _bag;

    /// <param name="definitions">dispositivos selecionáveis (um só = modo edição).</param>
    /// <param name="initialValues">valores iniciais (edição) ou nulo (padrões).</param>
    public ParameterForm(IReadOnlyList<DeviceDefinition> definitions, IReadOnlyDictionary<string, object>? initialValues,
        Func<DeviceDefinition, string> nameFor, string title, IReadOnlyList<string>? catalogErrors = null)
    {
        _nameFor = nameFor;
        Text = title;
        Width = 900;
        Height = 640;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Width = 90 };
        // sem CancelButton: Esc nunca fecha janela (padrão do painel); fecha pelo X ou por Cancelar
        AcceptButton = _ok;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(6) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_ok);

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 360 };
        right.Panel1.Controls.Add(_grid);
        right.Panel2.Controls.Add(_summary);

        if (definitions.Count > 1)
        {
            var main = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 230 };
            main.Panel1.Controls.Add(_devices);
            main.Panel2.Controls.Add(right);
            Controls.Add(main);
            foreach (var definition in definitions)
            {
                _devices.Items.Add(new Item(definition));
            }

            _devices.SelectedIndexChanged += (_, _) => Select(((Item)_devices.SelectedItem!).Definition, null);
        }
        else
        {
            Controls.Add(right);
        }

        Controls.Add(buttons);
        _grid.PropertyValueChanged += (_, _) =>
        {
            // um código de catálogo ou uma regra de ao_editar pode ter mudado outros parâmetros
            _grid.Refresh();
            UpdateSummary();
        };

        if (catalogErrors is { Count: > 0 })
        {
            _summary.Text = "Arquivos do catálogo com erro:\r\n" + string.Join("\r\n", catalogErrors);
        }

        if (definitions.Count == 1)
        {
            Select(definitions[0], initialValues);
        }
        else if (definitions.Count > 1)
        {
            _devices.SelectedIndex = 0;
        }
        else
        {
            _ok.Enabled = false;
        }
    }

    public DeviceDefinition? Definition => _bag?.Definition;

    public Dictionary<string, object> Values => _bag?.Values ?? new Dictionary<string, object>();

    public string InstanceName => _bag?.InstanceName ?? "";

    /// <summary>Instância avaliada com os valores atuais (válida quando OK está habilitado).</summary>
    public DeviceInstance? Instance { get; private set; }

    private void Select(DeviceDefinition definition, IReadOnlyDictionary<string, object>? values)
    {
        _bag = new ParameterBag(definition, values ?? new Dictionary<string, object>(), _nameFor(definition));
        _grid.SelectedObject = _bag;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (_bag is null)
        {
            return;
        }

        var text = new StringBuilder();
        var d = _bag.Definition;
        text.AppendLine($"{d.Name ?? d.Id}  ({d.Id} {d.Version}, {d.Category.ToString().ToLowerInvariant()})");
        if (!string.IsNullOrWhiteSpace(d.Description))
        {
            text.AppendLine(d.Description.Trim());
        }

        if (d.Family is not null)
        {
            text.AppendLine($"Família: {d.Family}{(d.Subtype is null ? "" : " — " + d.Subtype)}");
        }

        foreach (var message in _bag.LastMessages)
        {
            text.AppendLine("» " + message);
        }

        text.AppendLine();
        try
        {
            Instance = d.Evaluate(_bag.Values, _bag.LastEdited);
            _ok.Enabled = true;
            if (Instance.Code is not null)
            {
                text.AppendLine($"Código: {Instance.Code}");
            }

            foreach (var line in Instance.Log)
            {
                text.AppendLine(line);
            }
            foreach (var warning in Instance.Warnings)
            {
                text.AppendLine("AVISO: " + warning);
            }

            text.AppendLine("Variáveis:");
            foreach (var (name, value) in Instance.Variables.Where(v => v.Value is double or string or bool))
            {
                text.AppendLine($"  {name,-16} = {CoreValues.Format(value)}");
            }

            text.AppendLine("Quantitativos:");
            foreach (var q in Instance.Quantities)
            {
                text.AppendLine($"  {q.Name,-16} = {q.Value,12:0.0000} {q.Unit}");
            }

            text.AppendLine($"Sólidos: {string.Join(", ", Instance.Solids.Select(s => s.Name))}");
            text.AppendLine($"Conectores: {Instance.Connectors.Count}");
        }
        catch (ModelerException ex)
        {
            Instance = null;
            _ok.Enabled = false;
            text.AppendLine("ERRO: " + ex.Message);
        }

        _summary.Text = text.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
    }

    private sealed class Item
    {
        public Item(DeviceDefinition definition)
        {
            Definition = definition;
        }

        public DeviceDefinition Definition { get; }

        public override string ToString() => Definition.Name ?? Definition.Id;
    }
}
