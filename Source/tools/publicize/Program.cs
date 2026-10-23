// publicize - makes a REFERENCE-ONLY copy of a game assembly with every type, method and field public, so the
// HCT15 plugin can be compiled against members the game keeps private (the usual BepInEx modding practice).
// The copy is written to Mod\refs\ (git-ignored), used only by the compiler and never shipped; at run time the
// plugin talks to the player's own, unmodified assembly; Unity's Mono does not enforce member visibility at run
// time, which is what makes this standard BepInEx practice work (Mod/README.txt).
//
//   dotnet run --project Mod\tools\publicize -- <input.dll> <output.dll>
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: publicize <input.dll> <output.dll>"); return 2; }
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
        var asm = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { AssemblyResolver = resolver });
        int types = 0, methods = 0, fields = 0;
        foreach (TypeDefinition t in asm.MainModule.GetTypes())
        {
            if (t.IsNested) { if (!t.IsNestedPublic) { t.IsNestedPublic = true; types++; } }
            else if (!t.IsPublic) { t.IsPublic = true; types++; }
            foreach (MethodDefinition m in t.Methods)
                if (!m.IsPublic) { m.IsPublic = true; methods++; }
            var eventNames = t.Events.Select(e => e.Name).ToHashSet();
            foreach (FieldDefinition f in t.Fields)
            {
                if (f.IsPublic || eventNames.Contains(f.Name)) continue;   // an event's backing field would clash with the event
                f.IsPublic = true; fields++;
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));
        asm.Write(args[1]);
        Console.WriteLine(Path.GetFileName(args[0]) + ": " + types + " types, " + methods + " methods, " + fields + " fields made public -> " + args[1]);
        return 0;
    }
}
