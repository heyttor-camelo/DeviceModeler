using System;
using System.Collections.Generic;
using System.Linq;

namespace Modeler.Civil3D.Ui.Panel;

/// <summary>As categorias do painel, na ordem da proposta (estudo B §6.1).</summary>
internal static class Categories
{
    public static List<IPanelCategory> Create()
    {
        var devices = new DevicesCategory();
        return new List<IPanelCategory>
        {
            new NetworksCategory(),
            devices,
            new ComingSoonCategory("BACIAS", "BACIAS", "Bacias", "Bacias",
                "Bacias de contribuição: área, C, tempo de concentração e dispositivo de destino."),
            new ComingSoonCategory("SETORES", "SETORES", "Setores", "Setores",
                "Setores de consumo: população inicial e final, per capita e vazões."),
            new AuditCategory(devices),
            new ComingSoonCategory("SAIDAS", "SAIDAS", "Saídas", "Saidas",
                "Tabelas, rótulos e projeções em perfil dos dispositivos."),
            new ComingSoonCategory("SIMULACAO", "SIMULACAO", "Simulação", "Simulacao",
                "Itens do SWMM (curvas, séries, controles) e gráficos dos resultados."),
            new CatalogCategory(),
            new SettingsCategory(),
        };
    }
}

/// <summary>Categoria ainda sem conteúdo: só o aviso "em breve" e o que ela vai mostrar.</summary>
internal sealed class ComingSoonCategory : IPanelCategory
{
    private readonly string _description;

    public ComingSoonCategory(string id, string icon, string title, string keyword, string description)
    {
        Id = id;
        Icon = icon;
        Title = title;
        Keyword = keyword;
        _description = description;
    }

    public string Id { get; }

    public string Icon { get; }

    public string Title { get; }

    public string Keyword { get; }

    public bool Ready => false;

    public (int Count, int Warnings) Count => (-1, 0);

    public IReadOnlyList<PanelCommand> Commands => new[]
    {
        new PanelCommand("EMBREVE", Title + " (em breve)", "Geral", _description, Array.Empty<string>(), Run: () => { }),
    };

    public string[] Columns => new[] { "Item" };

    public double[] Widths => new double[] { 300 };

    public IReadOnlyList<PanelRow> Rows => Array.Empty<PanelRow>();

    public void Load(IPanelHost host)
    {
    }

    public IEnumerable<PanelTab> Tabs(IPanelHost host, PanelRow? row) =>
        new[] { new PanelTab("Propriedades", "Propriedades", () => new PanelUi(host.Theme).Section(Title).Note("Em breve. " + _description).Element) };

    public void Draw(MonitorView view, PanelRow? selected) => view.Message = $"{Title}\n\nEm breve: {_description}";

    public IEnumerable<PanelMenuItem> RowMenu(IPanelHost host, PanelRow row) => Enumerable.Empty<PanelMenuItem>();
}
