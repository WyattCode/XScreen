param([string]$PreviewDirectory = '')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot/..")
Add-Type -AssemblyName System.Drawing
# Vector geometry is rasterized independently at each physical Shell size.
# No scaled copy of the application PNG is used for any ICO member.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
public static class XScreenTrayArtwork {
  static GraphicsPath Round(float x,float y,float w,float h,float radius) {
    var p=new GraphicsPath(); float d=radius*2;
    p.AddArc(x,y,d,d,180,90); p.AddArc(x+w-d,y,d,d,270,90);
    p.AddArc(x+w-d,y+h-d,d,d,0,90); p.AddArc(x,y+h-d,d,d,90,90);
    p.CloseFigure(); return p;
  }
  public static Bitmap Render(int size) {
    var b=new Bitmap(size,size,PixelFormat.Format32bppArgb);
    using(var g=Graphics.FromImage(b)) {
      g.Clear(Color.Transparent); g.SmoothingMode=SmoothingMode.AntiAlias;
      // Sample pixel centers at n+0.5, so straight edges at integer coordinates
      // stay opaque instead of acquiring a half-alpha fringe on top/left.
      g.PixelOffsetMode=PixelOffsetMode.Half;
      using(var tile=Round(0,0,size,size,size*.23f))
      using(var pink=new LinearGradientBrush(new Point(0,0),new Point(size,size),Color.FromArgb(242,23,88),Color.FromArgb(192,0,56)))
        g.FillPath(pink,tile);
      int x=(int)Math.Round(size*.1875), y=(int)Math.Round(size*.25);
      int w=size-2*x, h=(int)Math.Round(size*.4375);
      int stroke=Math.Max(1,(int)Math.Round(size/14.0));
      using(var outline=Round(x,y,w,h,Math.Max(1,size*.055f))) {
        using(var inner=Round(x+stroke,y+stroke,w-2*stroke,h-2*stroke,Math.Max(.25f,size*.018f)))
          outline.AddPath(inner,false);
        outline.FillMode=FillMode.Alternate;
        using(var ink=new SolidBrush(Color.White)) g.FillPath(ink,outline);
      }
      int footW=(int)Math.Round(size*.3125); if((footW%2)!=(size%2)) footW++;
      int footY=(int)Math.Round(size*.8125)-stroke;
      using(var foot=Round((size-footW)/2,footY,footW,stroke,stroke/2f))
      using(var ink=new SolidBrush(Color.White)) g.FillPath(ink,foot);
    }
    return b;
  }
  public static byte[] Png(Bitmap b) { using(var m=new MemoryStream()) { b.Save(m,ImageFormat.Png); return m.ToArray(); } }
  public static void Write(string output,int[] sizes) {
    var images=new byte[sizes.Length][];
    for(int i=0;i<sizes.Length;i++) using(var b=Render(sizes[i])) images[i]=Png(b);
    using(var f=File.Create(output)) using(var w=new BinaryWriter(f)) {
      w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);
      int offset=6+16*sizes.Length;
      for(int i=0;i<sizes.Length;i++) {
        w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));
        w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write((ushort)32);
        w.Write(images[i].Length);w.Write(offset);offset+=images[i].Length;
      }
      foreach(var image in images)w.Write(image);
    }
  }
}
'@
$sizes = [int[]]@(16,20,24,28,32,36,40,48,56,64,96,128,256)
$output = Join-Path $root 'src/XScreen.App/Assets/XScreen.Tray.ico'
[XScreenTrayArtwork]::Write($output, $sizes)
if ($PreviewDirectory) {
    $preview = [IO.Path]::GetFullPath($PreviewDirectory)
    if (-not $preview.StartsWith((Join-Path $root 'analysis/evidence/'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Preview must stay in workspace evidence.' }
    if (Test-Path -LiteralPath $preview) { throw 'Use a fresh preview directory.' }
    $null = New-Item -ItemType Directory -Path $preview
    foreach ($size in $sizes) {
        $bitmap = [XScreenTrayArtwork]::Render($size)
        try { $bitmap.Save((Join-Path $preview "tray-$size.png"), [Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
    }
}
Write-Output "Generated $($sizes.Count) independently drawn tray frames: $output"
