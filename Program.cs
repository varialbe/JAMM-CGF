using UndertaleModLib;
using var fs = File.OpenRead(args[0]); var data = UndertaleIO.Read(fs,(w,_)=>{},_=>{});
foreach (var n in args.Skip(1)) { var o = int.TryParse(n, out int ix) ? data.GameObjects[ix] : data.GameObjects.FirstOrDefault(g=>g.Name.Content==n); if (o==null){Console.WriteLine($"{n}: missing");continue;}
 Console.WriteLine($"{n}: name={o.Name.Content} idx={data.GameObjects.IndexOf(o)} parent={o.ParentId?.Name.Content} sprite={o.Sprite?.Name.Content} events=" + string.Join(",", o.Events.SelectMany((l,i)=>l.Select(e=>$"{i}:{e.EventSubtype}:{string.Join("+",e.Actions.Select(a=>a.CodeId?.Name.Content))}")))); }
