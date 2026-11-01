using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Inklane.Controls;

internal sealed class PaperChoices : UniformGrid
{
    private int _selected;
    public event EventHandler? ChoiceChanged;
    public int SelectedIndex
    {
        get => _selected;
        set => ((RadioButton)Children[value]).IsChecked = true;
    }

    public PaperChoices(string name, params string[] choices)
    {
        Rows = 1; Columns = choices.Length;
        AutomationProperties.SetName(this,name);
        var group = Guid.NewGuid().ToString("N");
        for (int index = 0; index < choices.Length; index++)
        {
            int choice = index;
            var button = new RadioButton { Content = choices[index], GroupName = group, Height = 35, Margin = new Thickness(0,0,3,0) };
            button.SetResourceReference(StyleProperty,"PaperChoice");
            AutomationProperties.SetName(button,choices[index]);
            button.Checked += (_,_) => { _selected = choice; ChoiceChanged?.Invoke(this,EventArgs.Empty); };
            Children.Add(button);
        }
        SelectedIndex = 0;
    }
}
