using System.Text;

namespace Smithy.NET.CodeGeneration.CSharp;

internal sealed class CSharpWriter
{
    private const string IndentText = "    ";
    private readonly StringBuilder builder = new();
    private bool atLineStart = true;
    private int indent;

    public void Line()
    {
        builder.AppendLine();
        atLineStart = true;
    }

    public void Line(string text)
    {
        WriteIndentIfNeeded();
        builder.AppendLine(text);
        atLineStart = true;
    }

    public void Write(string text)
    {
        WriteIndentIfNeeded();
        builder.Append(text);
        atLineStart = false;
    }

    public void Block(Action body)
    {
        Block(body, closingSuffix: string.Empty);
    }

    public void Block(Action body, string closingSuffix)
    {
        ArgumentNullException.ThrowIfNull(body);

        Line("{");
        indent++;
        body();
        indent--;
        Line("}" + closingSuffix);
    }

    public void Indented(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        indent++;
        body();
        indent--;
    }

    public override string ToString()
    {
        return builder.ToString();
    }

    private void WriteIndentIfNeeded()
    {
        if (!atLineStart)
        {
            return;
        }

        for (var i = 0; i < indent; i++)
        {
            builder.Append(IndentText);
        }

        atLineStart = false;
    }
}
