using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Xml.Linq;

namespace Inklane;

internal static class TestResources
{
    internal static void Load(Application app)
    {
        // Copy the actual App.xaml resource declarations, not a second set of
        // test styles. This keeps theme regression coverage tied to production.
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppResources.xaml"));
        var resource = new XElement(source.Root!.Element(ns + "Application.Resources")!.Element(ns + "ResourceDictionary")!);
        resource.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
        var context = new ParserContext { BaseUri = new Uri("pack://application:,,,/Inklane;component/App.xaml") };
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(resource.ToString(), context));
    }
}
