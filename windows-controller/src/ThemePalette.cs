using System;
using System.Windows.Media;

namespace DeskMateLearning
{
    // Surfaces keep the seed hue at different intensities. Text roles use measured contrast,
    // not a fixed white foreground (especially important for mint, yellow and custom colours).
    internal sealed class ThemePalette
    {
        internal readonly Color Canvas,Sidebar,Grid,Tile,PrimaryTile,Selection,Strong,Ink,ButtonInk;
        static Color Mix(Color a,Color b,double t){return Color.FromRgb((byte)Math.Round(a.R+(b.R-a.R)*t),(byte)Math.Round(a.G+(b.G-a.G)*t),(byte)Math.Round(a.B+(b.B-a.B)*t));}
        static double Channel(byte value){double c=value/255.0;return c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4);}
        static double Light(Color c){return .2126*Channel(c.R)+.7152*Channel(c.G)+.0722*Channel(c.B);}
        internal static double Contrast(Color a,Color b){double x=Light(a)+.05,y=Light(b)+.05;return Math.Max(x,y)/Math.Min(x,y);}
        internal ThemePalette(Color seed){
            var white=Colors.White;Ink=Color.FromRgb(24,34,42);
            Canvas=Mix(white,seed,.085);Sidebar=Mix(white,seed,.22);Grid=Mix(white,seed,.14);
            Tile=Mix(white,seed,.28);PrimaryTile=Mix(white,seed,.48);Selection=Mix(white,seed,.35);
            // Deeper colour on the selected tile and navigation rail; lighter unselected tiles retain their hue.
            var deep=Mix(seed,Ink,.32);while(Contrast(deep,Selection)<4.5 || Contrast(deep,white)<4.5)deep=Mix(deep,Colors.Black,.1);
            Strong=deep;ButtonInk=Contrast(seed,Colors.Black)>Contrast(seed,white)?Colors.Black:white;
        }
    }
}
