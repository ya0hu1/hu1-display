using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace DeskMateLearning
{
    // The stream dimensions are a single measured run, never the tail of a clipped caption.
    // Use the same fonts and Graphics context for layout and paint, including at non-96 DPI.
    internal sealed class StudioHud : Form
    {
        internal sealed class Run { internal string Text; internal PointF Position; internal Font Font; }
        internal sealed class Strip { internal Size Size; internal readonly List<Run> Runs=new List<Run>(); }
        readonly Font regular=new Font("Segoe UI",9),emphasis=new Font("Segoe UI",10,FontStyle.Bold);
        static readonly Color surface=Color.FromArgb(31,37,41);
        TelemetrySnapshot snapshot; bool compact; int available;
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x80|0x08000000|0x20;return p;}}
        internal StudioHud(){
            AutoScaleMode=AutoScaleMode.None;StartPosition=FormStartPosition.Manual;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
            BackColor=surface;Opacity=.92;DoubleBuffered=true;
        }
        static string Value(double? n,bool fresh,string format){return fresh && n.HasValue?n.Value.ToString(format):"—";}
        internal static Rectangle Place(Rectangle work,int corner,int width,int height){int pad=Math.Min(14,Math.Min(work.Width,work.Height)/8);width=Math.Min(width,work.Width-2*pad);height=Math.Min(height,work.Height-2*pad);return new Rectangle(corner%2==0?work.Left+pad:work.Right-pad-width,corner<2?work.Top+pad:work.Bottom-pad-height,width,height);}
        internal static string[] Numbers(TelemetrySnapshot s){return new[]{Value(s.Fps,s.Fresh,"0.0"),Value(s.Mbps,s.Fresh,"0.0"),Value(s.RttMs,s.Fresh,"0")};}

        internal Strip Measure(Graphics g,TelemetrySnapshot s,bool single,int maxWidth){
            var strip=new Strip();var numbers=Numbers(s);float unit=g.DpiX/96f,pad=(float)Math.Ceiling(10*unit),gap=12*unit;
            float x=pad,y=5*unit,right=x,line=0;
            Action newLine=delegate {y+=line+2*unit;line=0;x=pad;};
            Action<string,string,Font> add=delegate(string text,string reserve,Font font){
                // Reserve common numeric widths so that the anchored bar stays still as FPS fluctuates.
                float width=(float)Math.Ceiling(Math.Max(g.MeasureString(text,font).Width,g.MeasureString(reserve,font).Width));
                if(x>pad && x+width+pad>maxWidth)newLine();
                strip.Runs.Add(new Run{Text=text,Position=new PointF(x,y),Font=font});
                right=Math.Max(right,x+width);line=Math.Max(line,(float)Math.Ceiling(g.MeasureString(text,font).Height));x+=width+gap;
            };
            add(numbers[0]+" FPS","88.8 FPS",emphasis);add(s.Width+" × "+s.Height,"",regular);
            if(!single)newLine();
            add(numbers[1]+" Mbps","88.8 Mbps",regular);add("RTT "+numbers[2]+" ms","RTT 88 ms",regular);
            if(!single){add(UiText.Get("目标")+" "+s.TargetHz+" fps","",regular);add(UiText.Get("丢帧")+" "+(s.HasStats?s.TotalDropped.ToString():"—"),"",regular);}
            strip.Size=Size.Ceiling(new SizeF(right+pad,y+line+5*unit));return strip;
        }
        internal static void PaintStrip(Graphics g,Strip strip,Size size){
            g.Clear(surface);var state=g.Save();try{
                // Last resort on unusually small work areas: fit the whole strip, not half a number.
                float fit=Math.Min(1f,Math.Min(size.Width/(float)strip.Size.Width,size.Height/(float)strip.Size.Height));
                g.ScaleTransform(fit,fit);g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
                foreach(var run in strip.Runs)g.DrawString(run.Text,run.Font,run.Font.Bold?Brushes.White:Brushes.Gainsboro,run.Position);
            }finally{g.Restore(state);}
        }
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(snapshot!=null)PaintStrip(e.Graphics,Measure(e.Graphics,snapshot,compact,available),ClientSize);}
        internal void Display(DesktopDisplay target,TelemetrySnapshot stats,int corner,bool single,double opacity){
            var monitor=target==null ? null : Screen.AllScreens.SingleOrDefault(s=>!s.Primary && s.DeviceName==target.Name);
            if(monitor==null || !target.Virtual || target.Primary || !stats.HasConfig){Hide();return;}
            var area=monitor.WorkingArea;available=Place(area,corner,area.Width,area.Height).Width;
            snapshot=stats;compact=single;Opacity=Math.Max(.5,Math.Min(1,opacity));
            using(var g=CreateGraphics()){var size=Measure(g,stats,single,available).Size;var next=Place(area,corner,size.Width,size.Height);if(Bounds!=next)Bounds=next;}
            if(!Visible)Show();Invalidate();
        }
        protected override void Dispose(bool disposing){if(disposing){regular.Dispose();emphasis.Dispose();}base.Dispose(disposing);}
    }
}
