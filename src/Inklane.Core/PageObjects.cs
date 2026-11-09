namespace Inklane.Core;

/// <summary>One stable paint order shared by desktop rendering and PDF export.</summary>
public static class PageObjects
{
    /// <summary>A copied page or component owns new identities, including its internal graph.</summary>
    public static void RegenerateIds(PageData page)
    {
        var ids = new Dictionary<string,string>(StringComparer.Ordinal);
        var groups = new Dictionary<string,string>(StringComparer.Ordinal);
        string Id(string value) => ids.TryGetValue(value,out var mapped) ? mapped : ids[value] = Guid.NewGuid().ToString("N");
        string Group(string value) => value.Length == 0 ? "" : groups.TryGetValue(value,out var mapped) ? mapped : groups[value] = Guid.NewGuid().ToString("N");
        foreach (var item in page.Images) { item.Id = Id(item.Id); item.GroupId = Group(item.GroupId); }
        foreach (var item in page.Ink) { item.Id = Id(item.Id); item.GroupId = Group(item.GroupId); }
        foreach (var item in page.Items) { item.Id = Id(item.Id); item.GroupId = Group(item.GroupId); }
        foreach (var item in page.Shapes)
        {
            item.Id = Id(item.Id); item.GroupId = Group(item.GroupId);
            if (item.StartId != null) item.StartId = Id(item.StartId);
            if (item.EndId != null) item.EndId = Id(item.EndId);
        }
    }

    public static IEnumerable<object> InPaintOrder(PageData page) =>
        page.Images.Select(item => (Item: (object)item, item.Layer, Kind: 0))
        .Concat(page.Ink.Select(item => (Item: (object)item, item.Layer, Kind: 1)))
        .Concat(page.Shapes.Select(item => (Item: (object)item, item.Layer, Kind: 2)))
        .Concat(page.Items.Select(item => (Item: (object)item, item.Layer, Kind: 3)))
        .OrderBy(pair => pair.Layer).ThenBy(pair => pair.Kind).Select(pair => pair.Item);
}
