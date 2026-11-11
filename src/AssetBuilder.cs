using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
class Assets {
 static void Main(string[] args) {
  var sizes = new[]{16,24,32,48,64,128,256}; var frames = new byte[sizes.Length][];
  for (int i = 0; i < sizes.Length; i++) using (var b = new Bitmap(sizes[i],sizes[i])) {
   using (var g = Graphics.FromImage(b)) { g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent); g.ScaleTransform(sizes[i]/256F,sizes[i]/256F); using (var bg = new SolidBrush(Color.FromArgb(0,122,111))) using (var shape = new GraphicsPath()) { shape.AddArc(8,8,64,64,180,90); shape.AddArc(184,8,64,64,270,90); shape.AddArc(184,184,64,64,0,90); shape.AddArc(8,184,64,64,90,90); shape.CloseFigure(); g.FillPath(bg,shape); } using (var pen = new Pen(Color.White,13)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; g.DrawLines(pen,new[]{new Point(60,142),new Point(60,192),new Point(196,192),new Point(196,142)}); g.DrawLine(pen,128,62,128,145); g.DrawLine(pen,99,115,128,145); g.DrawLine(pen,157,115,128,145); } }
   using (var m = new MemoryStream()) { b.Save(m,ImageFormat.Png); frames[i] = m.ToArray(); }
  }
  using (var writer = new BinaryWriter(File.Create(args[0]))) { writer.Write((short)0); writer.Write((short)1); writer.Write((short)sizes.Length); int offset = 6+16*sizes.Length; for (int i = 0; i < sizes.Length; i++) { writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)0); writer.Write((byte)0); writer.Write((short)1); writer.Write((short)32); writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length; } foreach (var f in frames) writer.Write(f); }
 }
}
