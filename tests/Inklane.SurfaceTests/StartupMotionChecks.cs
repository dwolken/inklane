using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Inklane;

namespace Inklane.SurfaceTests;

internal static partial class Program
{
    private static void StartupMotion()
    {
        foreach(var appearance in new[]{"Light","Dark"})
        {
            Settings.Current.Appearance=appearance;Appearance.Apply();
            var resources=Application.Current.Resources;
            Color Color(string key)=>((SolidColorBrush)resources[key]).Color;
            Assert(Contrast(Color("AccentBrush"),Color("AccentSoftBrush"))>=4.5,"Selected tools lost text contrast.");
            Assert(Contrast(Color("PrimaryBrush"),Color("OnPrimaryBrush"))>=4.5,"Primary button text lost contrast.");
            var window=new MainWindow {ShowInTaskbar=false,ShowActivated=false,Width=960,Height=700};
            window.Show();window.UpdateLayout();
            var mark=(FrameworkElement)Field(window,"_startupBrand")!;
            Call(window,"BeginStartupBrandMotion",true);
            var offset=(TranslateTransform)mark.RenderTransform;
            Assert(offset.HasAnimatedProperties,"Cold launch did not begin the brief mark motion.");
            var open=Descendants(window).OfType<Button>().First(b=>AutomationProperties.GetName(b)==L.T("OpenFile"));
            Assert(open.IsEnabled && open.IsVisible && open.IsHitTestVisible,"Startup motion blocked document access.");
            var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(400)};
            timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
            Assert(!offset.HasAnimatedProperties && mark.RenderTransform.Value.IsIdentity && mark.Opacity==1,"Startup clock did not return to the static brand.");
            Call(window,"ShowEditor");Call(window,"ShowHome");Call(window,"BeginStartupBrandMotion",true);
            Assert(mark.RenderTransform.Value.IsIdentity && mark.Opacity==1,"Returning Home replayed startup motion.");
            RenderContent((FrameworkElement)window.Content,window.Background,$"startup-{appearance.ToLowerInvariant()}.png");
            StopTimers(window);
        }
        var reduced=new MainWindow();Call(reduced,"BeginStartupBrandMotion",false);
        var reducedMark=(FrameworkElement)Field(reduced,"_startupBrand")!;
        Assert(reducedMark.RenderTransform.Value.IsIdentity && reducedMark.Opacity==1,"Reduced motion changed the static brand.");
        Call(reduced,"BeginStartupBrandMotion",true);Assert(reducedMark.RenderTransform.Value.IsIdentity,"A skipped startup replayed later.");
        StopTimers(reduced);
        var immediate=new MainWindow();Call(immediate,"BeginStartupBrandMotion",true);Call(immediate,"ShowEditor");
        var immediateMark=(FrameworkElement)Field(immediate,"_startupBrand")!;
        Assert(immediateMark.RenderTransform.Value.IsIdentity && immediateMark.Opacity==1,"Entering the editor left a startup animation running.");
        StopTimers(immediate);
        Settings.Current.Appearance="Light";Appearance.Apply();
    }
}
