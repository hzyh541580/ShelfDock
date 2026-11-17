using System;
using System.Windows.Forms;

namespace ShelfDock {
 // Hiding/recreating the shelf window must never end the tray's message loop.
 sealed class ShelfContext : ApplicationContext {
  readonly Shelf shelf; readonly Timer commands=new Timer { Interval=50 };
  internal ShelfContext(Shelf shelf,InstanceCoordinator instance,bool silent) {
   this.shelf=shelf;
   shelf.FormClosed+=(s,e)=>ExitThread();
   commands.Tick+=(s,e)=> {
    if(instance.ExitEvent.WaitOne(0)) { shelf.Quit(); return; }
    if(instance.ShowEvent.WaitOne(0)) shelf.ShowShelf();
   };
   shelf.InitializeVisibility();
   commands.Start();
   if(!silent) shelf.ShowShelf();
  }
  protected override void Dispose(bool disposing) {
   if(disposing) { commands.Dispose(); shelf.Dispose(); } base.Dispose(disposing);
  }
 }
}
