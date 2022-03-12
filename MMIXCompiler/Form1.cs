using ICSharpCode.TextEditor.Document;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using MMIXCompiler.Compiler;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace MMIXCompiler;

public partial class Form1 : Form
{
    readonly CodeGenerator c = new();
    readonly Random rnd = new();
    ImmutableArray<Diagnostic> errors = ImmutableArray<Diagnostic>.Empty;

    public Form1()
    {
        InitializeComponent();

        IHighlightingStrategy strategy = HighlightingStrategyFactory.CreateHighlightingStrategy("C#");
        textEditorControl1.Document.HighlightingStrategy = strategy;

        textEditorControl1.ActiveTextAreaControl.TextArea.ToolTipRequest += (s, e) =>
        {
            var strb = new StringBuilder();
            foreach (var err in errors)
            {
                var position = err.Location.GetLineSpan();
                if (
                    e.LogicalPosition.Line >= position.Span.Start.Line && e.LogicalPosition.Line <= position.Span.End.Line &&
                    e.LogicalPosition.Column >= position.Span.Start.Character && e.LogicalPosition.Column <= position.Span.End.Character
                    )
                {
                    strb.AppendFormat("{0}: {1}", err.Severity, err.GetMessage()).AppendLine();
                }
            }
            if (strb.Length > 0)
                e.ShowToolTip(strb.ToString());
        };

        try
        {
            var cur = Path.GetFullPath(".");
            var demo = File.ReadAllText("../../../../TestCode/Class1.cs");
            textEditorControl1.Text = demo;
        }
        catch { }
    }

    private void Button1_Click(object sender, EventArgs e)
    {
        var code = textEditorControl1.Text;
        var sourceTree = CSharpSyntaxTree.ParseText(SourceText.From(code));
        var mmixStd = CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText("../../../../MMIXStd/StdLib.cs")));

        var compilation = CSharpCompilation.Create($"mmix_{rnd.Next()}")
            .WithOptions(new CSharpCompilationOptions(

                outputKind: OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true))
            .AddReferences(
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            )
            .AddSyntaxTrees(mmixStd, sourceTree);

        using var ms_assembly = new MemoryStream();
        using var ms_pdb = new MemoryStream();
        var result = compilation.Emit(ms_assembly, ms_pdb,
            options: new EmitOptions()
                .WithDebugInformationFormat(DebugInformationFormat.PortablePdb)
            );

        if (result.Success)
        {
            ms_assembly.Seek(0, SeekOrigin.Begin);
            ms_pdb.Seek(0, SeekOrigin.Begin);

            using (Stream file = File.Create("mmix_dbg.dll"))
                ms_assembly.CopyTo(file);

            ms_assembly.Seek(0, SeekOrigin.Begin);
            ms_pdb.Seek(0, SeekOrigin.Begin);
            //var assembly = Assembly.Load(ms_assembly.ToArray(), ms_pdb.ToArray());
            //return InitializeAssembly(assembly);

            try
            {
                textBox2.Text = c.Compile(ms_assembly);
            }
            catch (Exception ex)
            {
                textBox2.Text = ex.ToString();
            }
        }
        else
        {
            textEditorControl1.Document.MarkerStrategy.RemoveAll(_ => true);
            var lineLengths = code.Split('\n').Select(x => x.Length + 1).ToList();
            var cumulLength = new List<int>(lineLengths.Count);
            int cum = 0;
            for (int i = 0; i < lineLengths.Count; i++)
            {
                cumulLength.Add(cum);
                cum += lineLengths[i];
            }

            errors = result.Diagnostics;
            foreach (var error in result.Diagnostics)
            {
                var position = error.Location.GetLineSpan();

                var start = cumulLength[position.Span.Start.Line] + position.Span.Start.Character;
                var end = cumulLength[position.Span.End.Line] + position.Span.End.Character;

                var marker = new TextMarker(start, end - start, TextMarkerType.WaveLine, Color.Red);
                textEditorControl1.Document.MarkerStrategy.AddMarker(marker);
            }
            textEditorControl1.Refresh();
        }
    }
}
