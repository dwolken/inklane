namespace Inklane.Core;

/// <summary>One stable paint order shared by desktop rendering and PDF export.</summary>
public static class PageObjects
{
    public static IEnumerable<object> InPaintOrder(PageData page) =>
        page.Images.Select(item => (Item: (object)item, item.Layer, Kind: 0))
        .Concat(page.Ink.Select(item => (Item: (object)item, item.Layer, Kind: 1)))
        .Concat(page.Shapes.Select(item => (Item: (object)item, item.Layer, Kind: 2)))
        .Concat(page.Items.Select(item => (Item: (object)item, item.Layer, Kind: 3)))
        .OrderBy(pair => pair.Layer).ThenBy(pair => pair.Kind).Select(pair => pair.Item);
}
