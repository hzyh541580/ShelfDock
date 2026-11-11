using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShelfDock {
 // Present only during a held-mouse movement while the shelf is hidden.
 // Actual OLE DragEnter/DragOver payloads, not that movement, reveal the shelf.
 sealed class EdgeDropZone : Form {
  readonly Shelf shelf;
  internal EdgeDropZone(Shelf owner) {
   shelf = owner; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
   StartPosition = FormStartPosition.Manual; TopMost = true; AllowDrop = true;
   BackColor = Color.Black; Opacity = 1.0/255;
   DragEnter += OnDrag; DragOver += OnDrag;
   DragDrop += (s,e) => { var point = new Point(e.X,e.Y); if (shelf.HandleEdgeDrag(e.Data,e.AllowedEffect,point) && shelf.Bounds.Contains(point)) { shelf.AcceptDrop(e.Data); e.Effect = DragDropEffects.Copy; } else e.Effect = DragDropEffects.None; Hide(); };
  }
  protected override bool ShowWithoutActivation { get { return true; } }
  protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080; return p; } }
  void OnDrag(object sender, DragEventArgs e) { var point = new Point(e.X,e.Y); e.Effect = shelf.HandleEdgeDrag(e.Data,e.AllowedEffect,point) && shelf.Bounds.Contains(point) ? DragDropEffects.Copy : DragDropEffects.None; }
 }
}
