using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Inklane.Controls;
using Inklane.Core;

namespace Inklane.LifecycleTests;

internal static partial class Program
{
    private static void ContinuousPagesJourney()
    {
        foreach (bool sourcePdf in new[] { false, true })
        {
            using var state = new WindowCase(sourcePdf ? "continuous-pdf" : "continuous-notebook");
            var file = state.Path("pages.pdf");
            if (sourcePdf) CreateSourcePdf(file);
            else DocumentStore.SavePdf(new DocumentData
            {
                Pages = [new PageData { Width = 700, Height = 1050 }, new PageData { Width = 1050, Height = 700, Pattern = PaperPattern.Squared }]
            }, file);
            string originalHash = DocumentStore.GetFileHash(file);
            var window = state.Window;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Width = 1040;
            window.Height = 650;
            window.Show();
            window.OpenFile(file);
            var document = Document(window);
            var surface = Field<DocumentSurface>(window,"_surface");
            var scroll = Field<ScrollViewer>(surface,"_scroll");
            var pageHost = Field<Grid>(surface,"_pageHost");
            Assert(ReferenceEquals(scroll.Content,pageHost),"Focused mode was not the default.");
            Call(window,"SetContinuousPages",true);
            window.UpdateLayout();
            var strip = (Canvas)scroll.Content;
            double pageTwo = Field<double[]>(surface,"_pageTops")[1];
            Assert(strip.Height > document.Pages[0].Height * surface.Zoom,"The scroll extent omits the other page.");
            scroll.ScrollToVerticalOffset(pageTwo - scroll.ViewportHeight * .7);
            window.UpdateLayout();
            WaitFor(() => strip.Children.OfType<Button>().Any(b => b.Content is Image),
                () => $"Source PDF={sourcePdf}, offset={scroll.VerticalOffset:F0}, page two={pageTwo:F0}, viewport={scroll.ViewportHeight:F0}, " +
                    $"active={Field<int>(window,"_pageIndex")}, previews={string.Join(", ",strip.Children.OfType<Button>().Select(b => b.Content?.ToString() ?? "(pending)"))}");
            Equal(0,Field<int>(window,"_pageIndex"));
            Assert(strip.Children.OfType<Button>().Any(),"The neighboring page was not visible.");
            strip.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Equal(1,Field<int>(window,"_pageIndex"));
            Assert(ReferenceEquals(document,Document(window)),"Changing pages duplicated the document model.");
            if (sourcePdf)
            {
                var pixel = Pixel(surface.RenderPage(),.6,.7);
                Assert(pixel[1] > pixel[0] + 50,"Continuous view displayed the wrong source page.");
            }
            double zoom = surface.Zoom;
            surface.ZoomBy(1.2);
            Assert(surface.Zoom > zoom && Canvas.GetTop(pageHost) > pageTwo,"Zoom did not scale the page strip.");
            Call(window,"GoToPage",0);
            window.UpdateLayout();
            Assert(scroll.VerticalOffset < 1,"Page navigation did not reveal the first page.");
            scroll.ScrollToEnd();
            window.UpdateLayout();
            WaitFor(() => Field<int>(window,"_pageIndex") == 1);

            var note = new TextItem { IsNote = true, IsExpanded = false, X = 100, Y = 110, Text = "Keep this anchor on page two." };
            document.Pages[1].Items.Add(note);
            Call(window,"ShowPage",false); Call(window,"DocumentChanged");
            Call(window,"SetContinuousPages",false);
            Equal(1,Field<int>(window,"_pageIndex"));
            Assert(ReferenceEquals(scroll.Content,pageHost),"Switching to focused mode left the page strip attached.");
            Equal(note.Id,Document(window).Pages[1].Items.Single().Id);
            var copy = sourcePdf ? file : state.Path("reviewed.pdf");
            if (!sourcePdf) state.Interaction.SavePaths.Enqueue(copy);
            Assert(Call<bool>(window,"Save",!sourcePdf),"Save failed after changing views.");
            window.OpenFile(copy); Call(window,"SetContinuousPages",true); Call(window,"GoToPage",1);
            var saved = Document(window).Pages[1].Items.Single();
            Equal(note.Id,saved.Id); Equal(note.X,saved.X); Equal(note.Y,saved.Y); Equal(note.Text,saved.Text);
            Equal(originalHash,DocumentStore.GetFileHash(file));
        }
    }

    private static void WaitFor(Func<bool> condition, Func<string>? detail = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(15);
        }
        Assert(condition(),$"The visible pages did not update. {detail?.Invoke()}");
    }
}
