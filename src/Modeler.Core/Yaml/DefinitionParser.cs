using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Modeler.Core.Definition;
using Modeler.Core.Errors;
using Modeler.Core.Expressions;
using Modeler.Core.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Modeler.Core.Yaml;

/// <summary>Converte o YAML em <see cref="DeviceDefinition"/>, validando estrutura, tipos,
/// expressões, nomes e dependências. Todo erro aponta arquivo, linha, coluna e campo.</summary>
internal sealed partial class DefinitionParser
{
    private static readonly string[] OutputFunctions = { "expor_solido", "conector", "connector" };

    private readonly string? _file;
    private readonly string[] _lines;
    private readonly FunctionRegistry _functions = FunctionRegistry.Default;
    private List<ParameterDef> _parameters = new();

    private DefinitionParser(string text, string? file)
    {
        _file = file;
        _lines = text.Split('\n');
    }

    public static DeviceDefinition Parse(string text, string? file)
    {
        var ExpressionParser = new DefinitionParser(text, file);
        var definition = ExpressionParser.ParseDocument(text);
        definition.SourceFile = file;
        definition.Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        return definition;
    }

    // ------------------------------------------------------------------ documento

    private DeviceDefinition ParseDocument(string text)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException ex)
        {
            var message = YamlLocationPrefix().Replace(ex.Message, "");
            if (ex.InnerException is not null && message.Length < 5)
            {
                message = ex.InnerException.Message;
            }

            throw new DefinitionException("erro de sintaxe YAML: " + message, Loc(ex.Start), null, ex);
        }

        if (stream.Documents.Count == 0)
        {
            throw new DefinitionException("arquivo de definição vazio", new SourceLocation(_file, 1, 1), null);
        }

        var root = Map(stream.Documents[0].RootNode, "(raiz)");
        var fields = Fields(root, "", "dispositivo", "nome", "versao", "categoria", "descricao",
            "familia", "subtipo", "codigo", "modelos", "parametros", "predefinicoes", "ao_editar",
            "variaveis", "validacoes", "fluxo", "saidas");

        var definition = new DeviceDefinition
        {
            Id = RequiredText(root, fields, "dispositivo", ""),
            Name = OptionalText(fields, "nome", ""),
            Version = RequiredText(root, fields, "versao", ""),
            Description = OptionalText(fields, "descricao", ""),
            Family = OptionalText(fields, "familia", ""),
            Subtype = OptionalText(fields, "subtipo", ""),
        };
        if (!IdentifierRegex().IsMatch(definition.Id))
        {
            throw new DefinitionException("o identificador do dispositivo deve conter só letras, números e '_' (ex.: pv_retangular)",
                Loc(fields["dispositivo"]), "dispositivo");
        }

        var categoryNode = fields.GetValueOrDefault("categoria") ?? throw Missing(root, "categoria", "");
        definition.Category = Normalize(Scalar(categoryNode, "categoria")) switch
        {
            "pontual" => DeviceCategory.Pontual,
            "linear" => DeviceCategory.Linear,
            var other => throw new DefinitionException($"categoria '{other}' inválida; use 'pontual' ou 'linear'", Loc(categoryNode), "categoria"),
        };

        var parameters = fields.TryGetValue("parametros", out var p) ? ParseParameters(p) : new List<ParameterDef>();
        definition.Parameters = parameters;
        _parameters = parameters;
        definition.Presets = fields.TryGetValue("predefinicoes", out var presets) ? ParsePresets(presets, parameters) : null;
        definition.EditRules = fields.TryGetValue("ao_editar", out var rules) ? ParseEditRules(rules, parameters) : new List<EditRuleDef>();

        var globals = new HashSet<string>(parameters.Select(x => x.Name), StringComparer.Ordinal);
        definition.Variables = fields.TryGetValue("variaveis", out var v) ? ParseVariables(v, globals) : new List<VariableDef>();
        globals.UnionWith(definition.Variables.Select(x => x.Name));

        foreach (var parameter in parameters)
        {
            if (parameter.Grip?.Position is { } gripPosition)
            {
                CheckNames(gripPosition, globals);
            }

            if (parameter.Grip?.Center is { } gripCenter)
            {
                CheckNames(gripCenter, globals);
            }
        }

        if (fields.TryGetValue("codigo", out var codeNode))
        {
            definition.CodeTemplate = Tmpl(codeNode, "codigo");
            CheckTemplate(definition.CodeTemplate, new HashSet<string>(globals.Append("familia").Append("subtipo"), StringComparer.Ordinal));
            if (!globals.Add("codigo"))
            {
                throw new DefinitionException("'codigo' já é parâmetro ou variável e conflita com o campo 'codigo' da definição", Loc(codeNode), "codigo");
            }
        }

        definition.Validations = fields.TryGetValue("validacoes", out var r) ? ParseValidations(r, globals) : new List<ValidationDef>();

        var known = new HashSet<string>(globals, StringComparer.Ordinal);
        definition.Flow = fields.TryGetValue("fluxo", out var f)
            ? ParseStatements(Sequence(f, "fluxo"), "fluxo", globals, known)
            : new List<Statement>();

        definition.Outputs = fields.TryGetValue("saidas", out var o) ? ParseOutputs(o, known) : new OutputsDef();
        if (fields.TryGetValue("modelos", out var templatesNode))
        {
            var visible = new HashSet<string>(globals.Concat(definition.Outputs.Quantities.Select(q => q.Name)).Concat(Results.DeviceInstance.TemplateNames), StringComparer.Ordinal);
            var t = Fields(Map(templatesNode, "modelos"), "modelos", "nome", "dica");
            definition.Templates = new TemplatesDef(
                t.TryGetValue("nome", out var nameNode) ? CheckTemplate(Tmpl(nameNode, "modelos.nome"), visible) : null,
                t.TryGetValue("dica", out var tipNode) ? CheckTemplate(Tmpl(tipNode, "modelos.dica"), visible) : null);
        }

        return definition;
    }

    // ------------------------------------------------------------------ predefinições e ao_editar

    private PresetsDef ParsePresets(YamlNode node, List<ParameterDef> parameters)
    {
        var map = Map(node, "predefinicoes");
        var fields = Fields(map, "predefinicoes", "parametro", "ao_editar_dimensao", "comuns", "itens");
        var name = RequiredText(map, fields, "parametro", "predefinicoes");
        var parameter = parameters.FirstOrDefault(x => x.Name == name)
                        ?? throw new DefinitionException($"parâmetro '{name}' não existe{Suggestion(name, parameters.Select(x => x.Name))}", Loc(fields["parametro"]), "predefinicoes.parametro");
        if (parameter.Type != ParameterType.Texto)
        {
            throw new DefinitionException($"o parâmetro do código ('{name}') deve ser do tipo texto", Loc(fields["parametro"]), "predefinicoes.parametro");
        }

        if (parameter.AllowedValues is not null)
        {
            throw new DefinitionException($"não declare 'valores' em '{name}': a lista é \"generico\" mais os códigos de 'predefinicoes.itens'",
                Loc(fields["parametro"]), "predefinicoes.parametro");
        }

        var mode = fields.TryGetValue("ao_editar_dimensao", out var modeNode)
            ? Normalize(Scalar(modeNode, "predefinicoes.ao_editar_dimensao")) switch
            {
                "generico" => PresetEditMode.Generico,
                "reaplicar" => PresetEditMode.Reaplicar,
                var other => throw new DefinitionException($"'{other}' inválido; use generico ou reaplicar", Loc(modeNode), "predefinicoes.ao_editar_dimensao"),
            }
            : PresetEditMode.Generico;

        IReadOnlyDictionary<string, object> ReadValues(YamlNode valuesNode, string field)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var (key, keyNode, valueNode) in Entries(Map(valuesNode, field)))
            {
                var target = parameters.FirstOrDefault(x => x.Name == key)
                             ?? throw new DefinitionException($"parâmetro '{key}' não existe{Suggestion(key, parameters.Select(x => x.Name))}", Loc(keyNode), $"{field}.{key}");
                if (target == parameter || target.Type == ParameterType.Conexoes)
                {
                    throw new DefinitionException($"'{key}' não pode ser preenchido por um código", Loc(keyNode), $"{field}.{key}");
                }

                object raw = target.IsNumeric ? Number(valueNode, $"{field}.{key}") : Scalar(valueNode, $"{field}.{key}");
                try
                {
                    result[key] = target.Coerce(raw);
                }
                catch (ParameterValidationException ex)
                {
                    throw new DefinitionException(ex.Reason, Loc(valueNode), $"{field}.{key}", ex);
                }
            }

            return result;
        }

        var common = fields.TryGetValue("comuns", out var commonNode) ? ReadValues(commonNode, "predefinicoes.comuns") : new Dictionary<string, object>();
        var itemsNode = fields.GetValueOrDefault("itens") ?? throw Missing(map, "itens", "predefinicoes");
        var items = new List<(string, IReadOnlyDictionary<string, object>)>();
        foreach (var (code, keyNode, valueNode) in Entries(Map(itemsNode, "predefinicoes.itens")))
        {
            if (Normalize(code) == PresetsDef.Generic)
            {
                throw new DefinitionException("'generico' é reservado (é o código das dimensões livres)", Loc(keyNode), "predefinicoes.itens");
            }

            items.Add((code, ReadValues(valueNode, $"predefinicoes.itens.{code}")));
        }

        if (items.Count == 0)
        {
            throw new DefinitionException("nenhum código em 'itens'", Loc(itemsNode), "predefinicoes.itens");
        }

        parameter.AllowedValues = items.Select(i => (object)i.Item1).Prepend(PresetsDef.Generic).ToList();
        if (!parameter.AllowedValues.Contains(parameter.Default))
        {
            throw new DefinitionException($"valor padrão '{parameter.Default}' não é um código: use generico ou {string.Join(", ", items.Select(i => i.Item1))}",
                parameter.Location, $"parametros.{name}.padrao");
        }

        return new PresetsDef(name, mode, common, items);
    }

    private List<EditRuleDef> ParseEditRules(YamlNode node, List<ParameterDef> parameters)
    {
        var names = new HashSet<string>(parameters.Select(x => x.Name), StringComparer.Ordinal);
        var result = new List<EditRuleDef>();
        foreach (var (edited, keyNode, valueNode) in Entries(Map(node, "ao_editar")))
        {
            var field = $"ao_editar.{edited}";
            if (!names.Contains(edited))
            {
                throw new DefinitionException($"parâmetro '{edited}' não existe{Suggestion(edited, names)}", Loc(keyNode), field);
            }

            var assignments = new List<(string, Expression)>();
            foreach (var (target, targetNode, expressionNode) in Entries(Map(valueNode, field)))
            {
                var parameter = parameters.FirstOrDefault(x => x.Name == target)
                                ?? throw new DefinitionException($"parâmetro '{target}' não existe{Suggestion(target, names)}", Loc(targetNode), $"{field}.{target}");
                if (target == edited)
                {
                    throw new DefinitionException($"a regra de '{edited}' não pode reescrever o próprio '{edited}'", Loc(targetNode), $"{field}.{target}");
                }

                if (parameter.Type == ParameterType.Conexoes)
                {
                    throw new DefinitionException($"'{target}' (conexões) não pode ser recalculado por ao_editar", Loc(targetNode), $"{field}.{target}");
                }

                var expression = Expr(expressionNode, $"{field}.{target}");
                CheckNames(expression, names);
                assignments.Add((target, expression));
            }

            result.Add(new EditRuleDef(edited, assignments, Loc(keyNode)));
        }

        return result;
    }

    // ------------------------------------------------------------------ parâmetros

    private List<ParameterDef> ParseParameters(YamlNode node)
    {
        var result = new List<ParameterDef>();
        foreach (var (name, keyNode, valueNode) in Entries(Map(node, "parametros")))
        {
            var field = $"parametros.{name}";
            CheckIdentifier(name, keyNode, field);
            var map = Map(valueNode, field);
            var fields = Fields(map, field, "tipo", "unidade", "padrao", "min", "max", "valores", "grip", "alvo", "rotulo", "grupo", "descricao");

            var typeNode = fields.GetValueOrDefault("tipo") ?? throw Missing(map, "tipo", field);
            var type = ParseType(typeNode, field + ".tipo");
            var unit = ParseParameterUnit(type, fields.GetValueOrDefault("unidade"), field + ".unidade");

            var parameter = new ParameterDef(name, type, unit, Loc(keyNode))
            {
                Label = OptionalText(fields, "rotulo", field),
                Group = OptionalText(fields, "grupo", field),
                Description = OptionalText(fields, "descricao", field),
            };

            var isNumeric = parameter.IsNumeric;
            foreach (var key in new[] { "min", "max", "grip" })
            {
                if (!isNumeric && fields.TryGetValue(key, out var invalid))
                {
                    throw new DefinitionException($"'{key}' só se aplica a parâmetros numéricos (tipo {Scalar(typeNode, field)})", Loc(invalid), $"{field}.{key}");
                }
            }

            parameter.Min = fields.TryGetValue("min", out var minNode) ? Number(minNode, field + ".min") : null;
            parameter.Max = fields.TryGetValue("max", out var maxNode) ? Number(maxNode, field + ".max") : null;
            if (parameter.Min > parameter.Max)
            {
                throw new DefinitionException($"min ({Values.FormatNumber(parameter.Min!.Value)}) maior que max ({Values.FormatNumber(parameter.Max!.Value)})",
                    Loc(minNode!), field + ".min");
            }

            if (fields.TryGetValue("valores", out var valuesNode))
            {
                if (type is ParameterType.Booleano or ParameterType.Conexoes)
                {
                    throw new DefinitionException("'valores' só se aplica a parâmetros numéricos ou de texto", Loc(valuesNode), field + ".valores");
                }

                parameter.AllowedValues = Sequence(valuesNode, field + ".valores")
                    .Select((item, i) => isNumeric ? (object)Number(item, $"{field}.valores[{i}]") : Scalar(item, $"{field}.valores[{i}]"))
                    .ToList();
                if (parameter.AllowedValues.Count == 0)
                {
                    throw new DefinitionException("lista de valores vazia", Loc(valuesNode), field + ".valores");
                }
            }

            parameter.Default = ParseDefault(parameter, fields.GetValueOrDefault("padrao"), map, field);
            parameter.Grip = fields.TryGetValue("grip", out var gripNode) ? ParseGrip(gripNode, field + ".grip", type) : null;
            parameter.Target = fields.TryGetValue("alvo", out var targetNode) ? ParseTarget(targetNode, field + ".alvo") : TargetKind.Nenhum;

            if (result.Any(x => x.Name == name))
            {
                throw new DefinitionException($"parâmetro '{name}' declarado duas vezes", Loc(keyNode), field);
            }

            result.Add(parameter);
        }

        return result;
    }

    private object ParseDefault(ParameterDef parameter, YamlNode? node, YamlMappingNode owner, string field)
    {
        object raw;
        if (node is null || node is YamlScalarNode { Value: null or "" } && parameter.Type != ParameterType.Texto)
        {
            raw = parameter.Type switch
            {
                ParameterType.Conexoes => Array.Empty<object>(),
                ParameterType.Booleano => false,
                ParameterType.Texto when parameter.AllowedValues is { Count: > 0 } values => values[0],
                _ => throw Missing(owner, "padrao", field),
            };
        }
        else if (parameter.Type == ParameterType.Conexoes)
        {
            raw = Sequence(node, field + ".padrao").Select((item, i) => (object)ParseConnection(item, $"{field}.padrao[{i}]")).ToList();
        }
        else if (parameter.IsNumeric)
        {
            raw = Number(node, field + ".padrao");
        }
        else
        {
            raw = Scalar(node, field + ".padrao");
        }

        try
        {
            return parameter.Coerce(raw);
        }
        catch (ParameterValidationException ex)
        {
            throw new DefinitionException("valor padrão inválido: " + ex.Reason, Loc(node ?? owner), field + ".padrao", ex);
        }
    }

    private Dictionary<string, object> ParseConnection(YamlNode node, string field)
    {
        var fields = Fields(Map(node, field), field, ParameterDef.ConnectionFields.ToArray());
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in fields)
        {
            result[key] = key is "nome" or "ref" ? Scalar(value, $"{field}.{key}") : Number(value, $"{field}.{key}");
        }

        return result;
    }

    private ParameterType ParseType(YamlNode node, string field) => Normalize(Scalar(node, field)) switch
    {
        "numero" or "real" => ParameterType.Numero,
        "inteiro" => ParameterType.Inteiro,
        "comprimento" or "distancia" => ParameterType.Comprimento,
        "cota" or "elevacao" => ParameterType.Cota,
        "area" => ParameterType.Area,
        "volume" => ParameterType.Volume,
        "vazao" => ParameterType.Vazao,
        "angulo" => ParameterType.Angulo,
        "texto" or "lista" => ParameterType.Texto,
        "booleano" or "logico" => ParameterType.Booleano,
        "conexoes" => ParameterType.Conexoes,
        var other => throw new DefinitionException(
            $"tipo '{other}' desconhecido; use numero, inteiro, comprimento, cota, area, volume, vazao, angulo, texto, booleano ou conexoes",
            Loc(node), field),
    };

    private string ParseParameterUnit(ParameterType type, YamlNode? node, string field)
    {
        var expected = type switch
        {
            ParameterType.Comprimento or ParameterType.Cota => "m",
            ParameterType.Area => "m2",
            ParameterType.Volume => "m3",
            ParameterType.Vazao => "m3/s",
            ParameterType.Angulo => "grau",
            _ => "",
        };
        if (node is null)
        {
            return expected;
        }

        var unit = ParseUnit(node, field);
        var free = type is ParameterType.Numero || (type is ParameterType.Inteiro && unit is "" or "un");
        if (!free && unit != expected)
        {
            var hint = expected.Length == 0 ? "este tipo não tem unidade" : $"o tipo exige '{expected}'";
            throw new DefinitionException($"unidade '{Scalar(node, field)}' incompatível: {hint} (o modelador trabalha em m, m², m³ e m³/s)", Loc(node), field);
        }

        return unit;
    }

    private string ParseUnit(YamlNode node, string field)
    {
        var text = Scalar(node, field);
        return text.Trim().ToLowerInvariant() switch
        {
            "" => "",
            "m" => "m",
            "m2" or "m²" => "m2",
            "m3" or "m³" => "m3",
            "m3/s" or "m³/s" => "m3/s",
            "grau" or "graus" or "°" => "grau",
            "un" or "und" => "un",
            "kg" => "kg",
            _ => throw new DefinitionException($"unidade '{text}' não suportada; use m, m2, m3, m3/s, grau, un ou kg", Loc(node), field),
        };
    }

    private GripDef ParseGrip(YamlNode node, string field, ParameterType type)
    {
        if (node is YamlScalarNode)
        {
            return new GripDef(ParseAxis(node, field), null, 1);
        }

        var map = Map(node, field);
        var fields = Fields(map, field, "tipo", "eixo", "posicao", "fator", "centro", "sentido");
        var kind = fields.TryGetValue("tipo", out var kindNode)
            ? Normalize(Scalar(kindNode, field + ".tipo")) switch
            {
                "esticar" => GripKind.Esticar,
                "rotacionar" or "girar" => GripKind.Rotacionar,
                "perfil" => GripKind.Perfil,
                var other => throw new DefinitionException($"tipo de grip '{other}' inválido; use esticar, rotacionar ou perfil", Loc(kindNode), field + ".tipo"),
            }
            : GripKind.Esticar;
        var position = fields.TryGetValue("posicao", out var positionNode) ? Expr(positionNode, field + ".posicao") : null;
        var factor = fields.TryGetValue("fator", out var factorNode) ? Number(factorNode, field + ".fator") : 1;
        void Forbid(string key, string reason)
        {
            if (fields.TryGetValue(key, out var invalid))
            {
                throw new DefinitionException($"'{key}' não se aplica a grip {reason}", Loc(invalid), $"{field}.{key}");
            }
        }

        switch (kind)
        {
            case GripKind.Rotacionar:
                Forbid("eixo", "de rotação (gira em planta)");
                Forbid("sentido", "de rotação");
                if (type != ParameterType.Angulo)
                {
                    throw new DefinitionException("grip de rotação só vale para parâmetro do tipo angulo", Loc(map), field);
                }

                return new GripDef("z", position ?? throw Missing(map, "posicao", field), factor)
                {
                    Kind = kind,
                    Center = fields.TryGetValue("centro", out var centerNode) ? Expr(centerNode, field + ".centro") : null,
                };
            case GripKind.Perfil:
                Forbid("eixo", "de perfil (anda na vertical)");
                Forbid("centro", "de perfil");
                var up = !fields.TryGetValue("sentido", out var directionNode) || Normalize(Scalar(directionNode, field + ".sentido")) switch
                {
                    "cima" => true,
                    "baixo" => false,
                    var other => throw new DefinitionException($"sentido '{other}' inválido; use cima ou baixo", Loc(directionNode), field + ".sentido"),
                };
                return new GripDef("z", position, factor) { Kind = kind, Up = up };
            default:
                Forbid("centro", "de esticar");
                Forbid("sentido", "de esticar");
                var axisNode = fields.GetValueOrDefault("eixo") ?? throw Missing(map, "eixo", field);
                return new GripDef(ParseAxis(axisNode, field + ".eixo"), position, factor);
        }
    }
    private string ParseAxis(YamlNode node, string field)
    {
        var axis = Scalar(node, field).Trim().ToLowerInvariant();
        return axis is "x" or "y" or "z"
            ? axis
            : throw new DefinitionException($"eixo '{axis}' inválido; use x, y ou z", Loc(node), field);
    }

    private TargetKind ParseTarget(YamlNode node, string field) => Normalize(Scalar(node, field)) switch
    {
        "" or "nenhum" => TargetKind.Nenhum,
        "superficie" => TargetKind.Superficie,
        "alinhamento" => TargetKind.Alinhamento,
        "perfil" => TargetKind.Perfil,
        "polilinha" => TargetKind.Polilinha,
        "tubo" => TargetKind.Tubo,
        var other => throw new DefinitionException($"alvo '{other}' inválido; use superficie, alinhamento, perfil, polilinha ou tubo", Loc(node), field),
    };

    // ------------------------------------------------------------------ variáveis e validações

    private List<VariableDef> ParseVariables(YamlNode node, HashSet<string> parameters)
    {
        var declared = new List<(VariableDef Def, YamlNode Key)>();
        foreach (var (name, keyNode, valueNode) in Entries(Map(node, "variaveis")))
        {
            var field = $"variaveis.{name}";
            CheckIdentifier(name, keyNode, field);
            if (parameters.Contains(name))
            {
                throw new DefinitionException($"'{name}' já é um parâmetro", Loc(keyNode), field);
            }

            Expression expression;
            string? unit = null;
            string? description = null;
            if (valueNode is YamlMappingNode map)
            {
                var fields = Fields(map, field, "valor", "unidade", "descricao");
                expression = Expr(fields.GetValueOrDefault("valor") ?? throw Missing(map, "valor", field), field);
                unit = fields.TryGetValue("unidade", out var u) ? ParseUnit(u, field + ".unidade") : null;
                description = OptionalText(fields, "descricao", field);
            }
            else
            {
                expression = Expr(valueNode, field);
            }

            declared.Add((new VariableDef(name, expression, unit, description), keyNode));
        }

        var all = new HashSet<string>(parameters.Concat(declared.Select(d => d.Def.Name)), StringComparer.Ordinal);
        foreach (var (def, _) in declared)
        {
            CheckNames(def.Expression, all);
        }

        var byName = declared.ToDictionary(d => d.Def.Name, d => d.Def, StringComparer.Ordinal);
        if (!DependencyResolver.TryOrder(declared.Select(d => d.Def.Name).ToList(), n => byName[n].Expression.FreeNames.Keys, out var order, out var cycle))
        {
            var first = byName[cycle[0]];
            throw new CircularDependencyException(cycle, first.Expression.Location, $"variaveis.{cycle[0]}");
        }

        return order.Select(n => byName[n]).ToList();
    }

    private List<ValidationDef> ParseValidations(YamlNode node, HashSet<string> globals)
    {
        var result = new List<ValidationDef>();
        var items = Sequence(node, "validacoes");
        for (var i = 0; i < items.Count; i++)
        {
            var field = $"validacoes[{i}]";
            var map = Map(items[i], field);
            var fields = Fields(map, field, "condicao", "mensagem", "nivel");
            var condition = Expr(fields.GetValueOrDefault("condicao") ?? throw Missing(map, "condicao", field), field + ".condicao");
            CheckNames(condition, globals);
            RequiredText(map, fields, "mensagem", field);
            var message = CheckTemplate(Tmpl(fields["mensagem"], field + ".mensagem"), globals);
            var level = fields.TryGetValue("nivel", out var levelNode)
                ? Normalize(Scalar(levelNode, field + ".nivel")) switch
                {
                    "erro" => ValidationLevel.Erro,
                    "aviso" => ValidationLevel.Aviso,
                    var other => throw new DefinitionException($"nivel '{other}' inválido; use erro ou aviso", Loc(levelNode), field + ".nivel"),
                }
                : ValidationLevel.Erro;
            result.Add(new ValidationDef(condition, message, level, Loc(map)));
        }

        return result;
    }

    // ------------------------------------------------------------------ fluxo

    private List<Statement> ParseStatements(IReadOnlyList<YamlNode> items, string path, HashSet<string> globals, HashSet<string> known)
    {
        var result = new List<Statement>();
        for (var i = 0; i < items.Count; i++)
        {
            var field = $"{path}[{i}]";
            var item = items[i];
            result.Add(item switch
            {
                YamlScalarNode scalar => ParseSimpleStatement(scalar, field, globals, known),
                YamlMappingNode map => ParseBlockStatement(map, field, globals, known),
                _ => throw new DefinitionException("instrução inválida: esperado texto ('nome = expressão') ou bloco 'se'/'para cada'", Loc(item), field),
            });
        }

        return result;
    }

    private Statement ParseSimpleStatement(YamlScalarNode node, string field, HashSet<string> globals, HashSet<string> known)
    {
        var text = node.Value ?? "";
        var assignment = AssignmentRegex().Match(text);
        if (assignment.Success)
        {
            var name = assignment.Groups[1].Value;
            var expression = Expr(node, field, assignment.Length);
            CheckNames(expression, known);
            if (globals.Contains(name))
            {
                throw new DefinitionException($"não é possível atribuir a '{name}': é parâmetro ou variável (somente leitura no fluxo)", Loc(node), field);
            }

            if (ExpressionParser.Keywords.Contains(name))
            {
                throw new DefinitionException($"'{name}' é palavra reservada", Loc(node), field);
            }

            known.Add(name);
            return new AssignStatement(name, expression, Loc(node), field);
        }

        var call = Expr(node, field);
        CheckNames(call, known);
        if (call.Root is not CallNode { Name: var function } || !OutputFunctions.Contains(function))
        {
            throw new DefinitionException(
                "instrução sem efeito: use 'nome = expressão', 'expor_solido(...)' ou 'conector(...)'", Loc(node), field);
        }

        return new CallStatement(call, Loc(node), field);
    }

    private Statement ParseBlockStatement(YamlMappingNode map, string field, HashSet<string> globals, HashSet<string> known)
    {
        var keys = map.Children.Keys.Select(k => Normalize(Scalar(k, field))).ToList();
        if (keys.Contains("se"))
        {
            var fields = Fields(map, field, normalizedKeys: true, "se", "entao", "senao");
            var condition = Expr(fields["se"], field + ".se");
            CheckNames(condition, known);
            var thenNode = fields.GetValueOrDefault("entao") ?? throw Missing(map, "entao", field);
            var then = ParseStatements(Sequence(thenNode, field + ".entao"), field + ".entao", globals, known);
            var otherwise = fields.TryGetValue("senao", out var elseNode)
                ? ParseStatements(Sequence(elseNode, field + ".senao"), field + ".senao", globals, known)
                : new List<Statement>();
            return new IfStatement(condition, then, otherwise, Loc(map), field);
        }

        if (keys.Contains("escolha"))
        {
            return ParseChoose(map, field, globals, known);
        }

        if (keys.Contains("mensagem"))
        {
            var fields = Fields(map, field, normalizedKeys: true, "mensagem");
            return new MessageStatement(CheckTemplate(Tmpl(fields["mensagem"], field + ".mensagem"), known), Loc(map), field);
        }

        if (keys.Contains("para cada") || keys.Contains("para_cada"))
        {
            var fields = Fields(map, field, normalizedKeys: true, "para cada", "para_cada", "faca");
            var headerNode = fields.GetValueOrDefault("para cada") ?? fields["para_cada"];
            var header = ForEachRegex().Match(Scalar(headerNode, field));
            if (!header.Success)
            {
                throw new DefinitionException("use 'para cada: item em lista' (ex.: 'c em Conexoes')", Loc(headerNode), field + ".para cada");
            }

            var variable = header.Groups[1].Value;
            if (globals.Contains(variable) || ExpressionParser.Keywords.Contains(variable))
            {
                throw new DefinitionException($"'{variable}' não pode ser usado como variável do laço (já é parâmetro, variável ou palavra reservada)",
                    Loc(headerNode), field + ".para cada");
            }

            var source = Expr(headerNode, field + ".para cada", header.Groups[3].Index);
            CheckNames(source, known);
            var bodyNode = fields.GetValueOrDefault("faca") ?? throw Missing(map, "faca", field);
            var wasKnown = !known.Add(variable);
            var body = ParseStatements(Sequence(bodyNode, field + ".faca"), field + ".faca", globals, known);
            if (!wasKnown)
            {
                known.Remove(variable);
            }

            return new ForEachStatement(variable, source, body, Loc(map), field);
        }

        throw new DefinitionException("bloco desconhecido: esperado 'se' (com 'entao'/'senao'), 'escolha' (com 'casos'), 'para cada' (com 'faca') ou 'mensagem'", Loc(map), field);
    }

    /// <summary><c>escolha: valor</c> + <c>casos</c> + <c>padrao</c>. Se o valor é um parâmetro com
    /// <c>valores</c>, as chaves são conferidas contra a lista e, sem <c>padrao</c>, todos os valores
    /// precisam de caso.</summary>
    private Statement ParseChoose(YamlMappingNode map, string field, HashSet<string> globals, HashSet<string> known)
    {
        var fields = Fields(map, field, normalizedKeys: true, "escolha", "casos", "padrao");
        var value = Expr(fields["escolha"], field + ".escolha");
        CheckNames(value, known);
        var parameter = value.Root is IdentifierNode id ? _parameters.FirstOrDefault(p => p.Name == id.Name) : null;
        var allowed = parameter?.AllowedValues;
        var casesNode = fields.GetValueOrDefault("casos") ?? throw Missing(map, "casos", field);
        var cases = new List<(object, IReadOnlyList<Statement>)>();
        foreach (var (keyText, keyNode, bodyNode) in Entries(Map(casesNode, field + ".casos")))
        {
            object key = parameter?.IsNumeric == true || (parameter is null && double.TryParse(keyText, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                ? Number(keyNode, $"{field}.casos")
                : keyText;
            if (allowed is not null && !allowed.Any(a => SameKey(a, key)))
            {
                throw new DefinitionException(
                    $"caso '{keyText}' não está nos valores de '{parameter!.Name}' ({string.Join(", ", allowed.Select(Values.Format))}){Suggestion(keyText, allowed.Select(Values.Format))}",
                    Loc(keyNode), $"{field}.casos.{keyText}");
            }

            if (cases.Any(c => SameKey(c.Item1, key)))
            {
                throw new DefinitionException($"caso '{keyText}' repetido", Loc(keyNode), $"{field}.casos");
            }

            cases.Add((key, ParseStatements(Sequence(bodyNode, $"{field}.casos.{keyText}"), $"{field}.casos.{keyText}", globals, known)));
        }

        var otherwise = fields.TryGetValue("padrao", out var defaultNode)
            ? ParseStatements(Sequence(defaultNode, field + ".padrao"), field + ".padrao", globals, known)
            : null;
        if (otherwise is null && allowed is not null)
        {
            var missing = allowed.Where(a => !cases.Any(c => SameKey(c.Item1, a))).Select(Values.Format).ToList();
            if (missing.Count > 0)
            {
                throw new DefinitionException($"faltam casos para {string.Join(", ", missing)} (ou declare 'padrao')", Loc(map), field);
            }
        }

        return new ChooseStatement(value, cases, otherwise, Loc(map), field);
    }

    private static bool SameKey(object a, object b) => (a, b) switch
    {
        (double x, double y) => Values.NearlyEqual(x, y),
        (string x, string y) => x == y,
        _ => false,
    };

    // ------------------------------------------------------------------ saídas

    private OutputsDef ParseOutputs(YamlNode node, HashSet<string> known)
    {
        var fields = Fields(Map(node, "saidas"), "saidas", "solidos", "conectores", "hidraulica", "quantitativos");
        var outputs = new OutputsDef();

        if (fields.TryGetValue("solidos", out var solidsNode))
        {
            outputs.Solids = Entries(Map(solidsNode, "saidas.solidos")).Select(e =>
            {
                var field = $"saidas.solidos.{e.Key}";
                Expression solid;
                string? material = null;
                if (e.Value is YamlMappingNode map)
                {
                    var f = Fields(map, field, "solido", "material");
                    solid = Expr(f.GetValueOrDefault("solido") ?? throw Missing(map, "solido", field), field + ".solido");
                    material = OptionalText(f, "material", field);
                }
                else
                {
                    solid = Expr(e.Value, field);
                }

                CheckNames(solid, known);
                return new SolidOutputDef(e.Key, solid, material, Loc(e.KeyNode));
            }).ToList();
        }

        if (fields.TryGetValue("conectores", out var connectorsNode))
        {
            outputs.Connectors = Sequence(connectorsNode, "saidas.conectores").Select((item, i) =>
            {
                var field = $"saidas.conectores[{i}]";
                var map = Map(item, field);
                var f = Fields(map, field, "nome", "posicao", "direcao", "diametro", "filtro");
                Expression Required(string key)
                {
                    var e = Expr(f.GetValueOrDefault(key) ?? throw Missing(map, key, field), $"{field}.{key}");
                    CheckNames(e, known);
                    return e;
                }

                return new ConnectorOutputDef(RequiredText(map, f, "nome", field), Required("posicao"), Required("direcao"), Required("diametro"),
                    OptionalText(f, "filtro", field), Loc(map));
            }).ToList();
        }

        if (fields.TryGetValue("hidraulica", out var hydraulicNode))
        {
            outputs.Hydraulics = Entries(Map(hydraulicNode, "saidas.hidraulica")).Select(e =>
            {
                var field = $"saidas.hidraulica.{e.Key}";
                if (e.Key != "swmm_juncao")
                {
                    throw new DefinitionException($"nó hidráulico '{e.Key}' não suportado (disponível: swmm_juncao)", Loc(e.KeyNode), field);
                }

                var map = Map(e.Value, field);
                var f = Fields(map, field, "cota_fundo", "profundidade_maxima", "profundidade_inicial", "sobrecarga", "area_alagamento");
                Expression? Optional(string key)
                {
                    if (!f.TryGetValue(key, out var n))
                    {
                        return null;
                    }

                    var expr = Expr(n, $"{field}.{key}");
                    CheckNames(expr, known);
                    return expr;
                }

                return (HydraulicDef)new SwmmJunctionDef(
                    Optional("cota_fundo") ?? throw Missing(map, "cota_fundo", field),
                    Optional("profundidade_maxima") ?? throw Missing(map, "profundidade_maxima", field),
                    Optional("profundidade_inicial"),
                    Optional("sobrecarga"),
                    Optional("area_alagamento"),
                    Loc(e.KeyNode));
            }).ToList();
        }

        if (fields.TryGetValue("quantitativos", out var quantitiesNode))
        {
            outputs.Quantities = Entries(Map(quantitiesNode, "saidas.quantitativos")).Select(e =>
            {
                var field = $"saidas.quantitativos.{e.Key}";
                Expression value;
                string? unit = null;
                string? description = null;
                if (e.Value is YamlMappingNode map)
                {
                    var f = Fields(map, field, "valor", "unidade", "descricao");
                    value = Expr(f.GetValueOrDefault("valor") ?? throw Missing(map, "valor", field), field + ".valor");
                    unit = f.TryGetValue("unidade", out var u) ? ParseUnit(u, field + ".unidade") : null;
                    description = OptionalText(f, "descricao", field);
                }
                else
                {
                    value = Expr(e.Value, field);
                }

                CheckNames(value, known);
                return new QuantityDef(e.Key, value, unit ?? UnitFromSuffix(e.Key), description, Loc(e.KeyNode));
            }).ToList();
        }

        return outputs;
    }

    private static string UnitFromSuffix(string name)
    {
        var suffix = name[(name.LastIndexOf('_') + 1)..].ToLowerInvariant();
        return name.Contains('_') && suffix is "m" or "m2" or "m3" or "un" or "kg" ? suffix : "";
    }

    // ------------------------------------------------------------------ verificação de nomes

    private void CheckNames(Expression expression, HashSet<string> known)
    {
        foreach (var (name, position) in expression.FreeNames)
        {
            if (!known.Contains(name) && !Evaluator.Constants.ContainsKey(name))
            {
                throw new DefinitionException($"nome desconhecido '{name}'{Suggestion(name, known)}", expression.LocationAt(position), expression.Field);
            }
        }

        CheckFunctions(expression.Root, expression);
    }

    private void CheckFunctions(Node node, Expression expression)
    {
        switch (node)
        {
            case CallNode call:
                if (!_functions.Contains(call.Name))
                {
                    var names = _functions.Names.Concat(new[] { "se", "existe", "ou" });
                    throw new DefinitionException($"função desconhecida '{call.Name}'{Suggestion(call.Name, names)}",
                        expression.LocationAt(call.Position), expression.Field);
                }

                call.Arguments.ToList().ForEach(a => CheckFunctions(a.Value, expression));
                break;
            case ListNode l:
                l.Items.ToList().ForEach(i => CheckFunctions(i, expression));
                break;
            case MemberNode m:
                CheckFunctions(m.Target, expression);
                break;
            case IndexNode ix:
                CheckFunctions(ix.Target, expression);
                CheckFunctions(ix.Index, expression);
                break;
            case UnaryNode u:
                CheckFunctions(u.Operand, expression);
                break;
            case BinaryNode b:
                CheckFunctions(b.Left, expression);
                CheckFunctions(b.Right, expression);
                break;
        }
    }

    private static string Suggestion(string name, IEnumerable<string> candidates)
    {
        var lower = name.ToLowerInvariant();
        var best = candidates
            .Select(c => (Name: c, Distance: Levenshtein(lower, c.ToLowerInvariant())))
            // prefixo com 3+ letras também conta ("minimo" → "min")
            .Where(c => c.Distance <= Math.Max(1, name.Length / 3) ||
                        (Math.Min(lower.Length, c.Name.Length) >= 3 &&
                         (lower.StartsWith(c.Name.ToLowerInvariant(), StringComparison.Ordinal) ||
                          c.Name.ToLowerInvariant().StartsWith(lower, StringComparison.Ordinal))))
            .OrderBy(c => c.Distance)
            .FirstOrDefault();
        return best.Name is null ? "" : $" (você quis dizer '{best.Name}'?)";
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[a.Length, b.Length];
    }

    private void CheckIdentifier(string name, YamlNode keyNode, string field)
    {
        if (!IdentifierRegex().IsMatch(name))
        {
            throw new DefinitionException($"nome '{name}' inválido: use letras, números e '_' (sem espaços, começando por letra)", Loc(keyNode), field);
        }

        if (ExpressionParser.Keywords.Contains(name) || Evaluator.Constants.ContainsKey(name))
        {
            throw new DefinitionException($"'{name}' é palavra reservada", Loc(keyNode), field);
        }
    }

    // ------------------------------------------------------------------ utilitários YAML

    private SourceLocation Loc(YamlNode node) => Loc(node.Start);

    private SourceLocation Loc(Mark mark) => new(_file, (int)mark.Line, (int)mark.Column);

    private Expression Expr(YamlNode node, string field, int offset = 0)
    {
        if (node is not YamlScalarNode scalar)
        {
            throw new DefinitionException("esperada uma expressão (texto)", Loc(node), field);
        }

        var text = scalar.Value ?? "";
        var location = Loc(node);
        var shift = 0;
        switch (scalar.Style)
        {
            case ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted:
                shift = 1;
                break;
            case ScalarStyle.Literal or ScalarStyle.Folded:
                location = BlockScalarStart(location);
                break;
        }

        if (offset >= text.Length || string.IsNullOrWhiteSpace(text[offset..]))
        {
            throw new DefinitionException("expressão vazia", location, field);
        }

        return Expression.Parse(text[offset..], location, field, shift + offset);
    }

    /// <summary>Modelo de texto (<c>{expressão:formato}</c>) com a posição no arquivo.</summary>
    private Template Tmpl(YamlNode node, string field)
    {
        if (node is not YamlScalarNode scalar)
        {
            throw new DefinitionException("esperado um texto", Loc(node), field);
        }

        var location = Loc(node);
        var shift = scalar.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted ? 1 : 0;
        if (scalar.Style is ScalarStyle.Literal or ScalarStyle.Folded)
        {
            location = BlockScalarStart(location);
        }

        return Template.Parse(scalar.Value ?? "", location, field, shift);
    }

    private Template CheckTemplate(Template template, HashSet<string> known)
    {
        foreach (var expression in template.Expressions)
        {
            CheckNames(expression, known);
        }

        return template;
    }

    /// <summary>Em escalares de bloco (| ou >) o conteúdo começa na linha seguinte, após a indentação.</summary>
    private SourceLocation BlockScalarStart(SourceLocation indicator)
    {
        for (var line = indicator.Line; line < _lines.Length; line++)
        {
            var content = _lines[line];
            if (!string.IsNullOrWhiteSpace(content))
            {
                return indicator with { Line = line + 1, Column = content.Length - content.TrimStart().Length + 1 };
            }
        }

        return indicator;
    }

    private YamlMappingNode Map(YamlNode node, string field) =>
        node as YamlMappingNode ?? throw new DefinitionException($"esperado um mapeamento (campo: valor), encontrado {Describe(node)}", Loc(node), field);

    private IReadOnlyList<YamlNode> Sequence(YamlNode node, string field) => node switch
    {
        YamlSequenceNode seq => seq.Children.ToList(),
        YamlScalarNode { Value: null or "" } => Array.Empty<YamlNode>(),
        _ => throw new DefinitionException($"esperada uma lista (itens com '-'), encontrado {Describe(node)}", Loc(node), field),
    };

    private string Scalar(YamlNode node, string field) =>
        node is YamlScalarNode scalar
            ? scalar.Value ?? ""
            : throw new DefinitionException($"esperado um valor simples, encontrado {Describe(node)}", Loc(node), field);

    private double Number(YamlNode node, string field)
    {
        var text = Scalar(node, field).Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
        {
            return value;
        }

        var hint = text.Contains(',') ? " (use ponto como separador decimal: 1.20)" : "";
        throw new DefinitionException($"esperado número, encontrado '{text}'{hint}", Loc(node), field);
    }

    private IEnumerable<(string Key, YamlNode KeyNode, YamlNode Value)> Entries(YamlMappingNode map)
    {
        foreach (var (key, value) in map.Children)
        {
            yield return (Scalar(key, "(chave)"), key, value);
        }
    }

    private Dictionary<string, YamlNode> Fields(YamlMappingNode map, string field, params string[] allowed) =>
        Fields(map, field, false, allowed);

    /// <summary>Lê as chaves de um mapeamento, rejeitando as desconhecidas.
    /// Com <paramref name="normalizedKeys"/>, acentos são ignorados (então/entao, faça/faca).</summary>
    private Dictionary<string, YamlNode> Fields(YamlMappingNode map, string field, bool normalizedKeys, params string[] allowed)
    {
        var result = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        foreach (var (key, keyNode, value) in Entries(map))
        {
            var name = normalizedKeys ? Normalize(key) : key;
            if (!allowed.Contains(name))
            {
                var prefix = field.Length == 0 ? "" : field + ".";
                throw new DefinitionException(
                    $"campo desconhecido '{key}'{Suggestion(key, allowed)}; campos válidos: {string.Join(", ", allowed)}", Loc(keyNode), prefix + key);
            }

            if (!result.TryAdd(name, value))
            {
                throw new DefinitionException($"campo '{key}' repetido", Loc(keyNode), field);
            }
        }

        return result;
    }

    private string RequiredText(YamlMappingNode map, Dictionary<string, YamlNode> fields, string key, string field)
    {
        var path = field.Length == 0 ? key : $"{field}.{key}";
        var text = fields.TryGetValue(key, out var node) ? Scalar(node, path).Trim() : "";
        return text.Length > 0 ? text : throw Missing(map, key, field);
    }

    private string? OptionalText(Dictionary<string, YamlNode> fields, string key, string field) =>
        fields.TryGetValue(key, out var node) ? Scalar(node, field.Length == 0 ? key : $"{field}.{key}").Trim() : null;

    private DefinitionException Missing(YamlMappingNode map, string key, string field) =>
        new($"campo obrigatório '{key}' ausente", Loc(map), field.Length == 0 ? key : $"{field}.{key}");

    private static string Describe(YamlNode node) => node switch
    {
        YamlScalarNode s => $"'{s.Value}'",
        YamlSequenceNode => "uma lista",
        YamlMappingNode => "um mapeamento",
        _ => node.NodeType.ToString(),
    };

    /// <summary>Minúsculas, sem acentos e com espaços simples: "Então" → "entao".</summary>
    private static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return WhitespaceRegex().Replace(sb.ToString().Normalize(NormalizationForm.FormC), " ");
    }

    [GeneratedRegex(@"^[\p{L}_][\p{L}\p{Nd}_]*$")]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"^\s*([\p{L}_][\p{L}\p{Nd}_]*)\s*=(?!=)")]
    private static partial Regex AssignmentRegex();

    [GeneratedRegex(@"^\s*([\p{L}_][\p{L}\p{Nd}_]*)\s+(em|in)\s+(.+)$", RegexOptions.Singleline)]
    private static partial Regex ForEachRegex();

    [GeneratedRegex(@"^\(Line: \d+, Col: \d+, Idx: \d+\) - \(Line: \d+, Col: \d+, Idx: \d+\):\s*")]
    private static partial Regex YamlLocationPrefix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
