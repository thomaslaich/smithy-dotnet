package io.github.thomaslaich.nsmithy.codegen.core;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Renders Smithy documentation as the lines of a .NET XML documentation comment, without the {@code
 * ///} prefix.
 *
 * <p>Smithy documentation is CommonMark, but the AWS models carry HTML. Text that uses an HTML tag
 * this class knows is converted to the XML documentation vocabulary: {@code <p>} becomes {@code
 * <para>}, lists become {@code <list>}, {@code <code>} becomes {@code <c>}, links become {@code
 * <see href>}, and so on. Any other tag inside such text is kept as literal text, so placeholders
 * like {@code <account-id>} survive. Text without a known tag is escaped line by line, as before.
 */
@SmithyInternalApi
public final class XmlDocMarkup {

  private static final int WIDTH = 100;
  private static final String ROOT = "#root";

  private static final Set<String> PARAGRAPHS = Set.of("p");
  private static final Set<String> HEADINGS =
      Set.of("h1", "h2", "h3", "h4", "h5", "h6", "fullname");
  private static final Set<String> LISTS = Set.of("ul", "ol", "dl");
  private static final Set<String> LIST_ITEMS = Set.of("li", "dt", "dd");
  private static final Set<String> ADMONITIONS = Set.of("note", "important");
  private static final Set<String> PREFORMATTED = Set.of("pre");
  private static final Set<String> INLINE =
      Set.of("code", "a", "b", "strong", "i", "em", "replaceable", "u", "br");
  private static final Set<String> VOID = Set.of("br");
  private static final Set<String> KNOWN = union();

  private static final Pattern KNOWN_TAG =
      Pattern.compile(
          "</?(?:" + String.join("|", KNOWN) + ")(?:\\s[^<>]*)?/?>", Pattern.CASE_INSENSITIVE);
  private static final Pattern TAG =
      Pattern.compile("<(/?)([A-Za-z][\\w:-]*)((?:\\s[^<>]*)?)(/?)>");
  private static final Pattern COMMENT = Pattern.compile("<!--.*?-->", Pattern.DOTALL);
  private static final Pattern HREF =
      Pattern.compile("\\bhref\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')", Pattern.CASE_INSENSITIVE);
  private static final Pattern ENTITY = Pattern.compile("&(#[xX][0-9a-fA-F]+|#[0-9]+|[a-zA-Z]+);");
  private static final Pattern WHITESPACE = Pattern.compile("\\s+");

  private XmlDocMarkup() {}

  private static Set<String> union() {
    var all = new java.util.HashSet<String>();
    all.addAll(PARAGRAPHS);
    all.addAll(HEADINGS);
    all.addAll(LISTS);
    all.addAll(LIST_ITEMS);
    all.addAll(ADMONITIONS);
    all.addAll(PREFORMATTED);
    all.addAll(INLINE);
    return Set.copyOf(all);
  }

  /** The documentation as XML documentation lines; a blank line is an empty string. */
  public static List<String> render(String documentation) {
    if (documentation == null || documentation.isBlank()) {
      return List.of();
    }
    if (!KNOWN_TAG.matcher(documentation).find()) {
      return plain(documentation);
    }
    Node root = parse(COMMENT.matcher(documentation).replaceAll(""));
    List<String> lines = new ArrayList<>();
    renderBlocks(root.children, lines, null, true);
    return lines;
  }

  /** Escapes text for element content. */
  public static String escape(String text) {
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;");
  }

  private static List<String> plain(String documentation) {
    List<String> lines = new ArrayList<>();
    for (String line : documentation.strip().split("\\R", -1)) {
      lines.add(line.isBlank() ? "" : escape(line.strip()));
    }
    return lines;
  }

  // Parsing

  private static final class Node {
    final String name; // null for text
    final String text; // text content, or the href of a link
    final List<Node> children = new ArrayList<>();

    Node(String name, String text) {
      this.name = name;
      this.text = text;
    }

    boolean isText() {
      return name == null;
    }

    boolean isInline() {
      return isText() || INLINE.contains(name);
    }
  }

  private static Node parse(String html) {
    Node root = new Node(ROOT, null);
    Deque<Node> open = new ArrayDeque<>();
    open.push(root);
    Matcher m = TAG.matcher(html);
    int last = 0;
    while (m.find()) {
      if (m.start() > last) {
        open.peek().children.add(new Node(null, html.substring(last, m.start())));
      }
      last = m.end();
      String name = m.group(2).toLowerCase(Locale.ROOT);
      if (!KNOWN.contains(name)) {
        // Not markup: a placeholder such as <account-id>, kept as text.
        open.peek().children.add(new Node(null, m.group()));
        continue;
      }
      boolean closing = !m.group(1).isEmpty();
      if (closing) {
        if (open.stream().anyMatch(n -> name.equals(n.name))) {
          while (!name.equals(open.peek().name)) {
            open.pop();
          }
          open.pop();
        }
        continue;
      }
      if ((PARAGRAPHS.contains(name) || LIST_ITEMS.contains(name))
          && name.equals(open.peek().name)) {
        // A sibling opened before the previous one was closed.
        open.pop();
      }
      String href = null;
      if (name.equals("a")) {
        Matcher h = HREF.matcher(m.group(3));
        if (h.find()) {
          href = h.group(1) != null ? h.group(1) : h.group(2);
        }
      }
      Node element = new Node(name, href);
      open.peek().children.add(element);
      boolean selfClosing = !m.group(4).isEmpty() || VOID.contains(name);
      if (!selfClosing) {
        open.push(element);
      }
    }
    if (last < html.length()) {
      open.peek().children.add(new Node(null, html.substring(last)));
    }
    return root;
  }

  // Rendering

  /**
   * Renders a block container's children. Consecutive inline nodes form a paragraph. With {@code
   * allowBare}, a lone paragraph is emitted without {@code <para>}. {@code leadIn} is prepended to
   * the first paragraph.
   */
  private static void renderBlocks(
      List<Node> children, List<String> lines, String leadIn, boolean allowBare) {
    List<Object> units = new ArrayList<>(); // Node for a block element, String for a paragraph
    List<Node> run = new ArrayList<>();
    for (Node child : children) {
      if (child.isInline()) {
        run.add(child);
        continue;
      }
      addParagraph(units, run);
      if (PARAGRAPHS.contains(child.name)) {
        addParagraph(units, child.children);
      } else if (HEADINGS.contains(child.name)) {
        String text = inline(child.children);
        if (!text.isEmpty()) {
          units.add("<b>" + text + "</b>");
        }
      } else if (LIST_ITEMS.contains(child.name)) {
        // A stray item outside its list: render its content in place.
        addParagraph(units, child.children);
      } else {
        units.add(child);
      }
    }
    addParagraph(units, run);

    boolean bare = allowBare && units.size() == 1 && units.get(0) instanceof String;
    boolean first = true;
    for (Object unit : units) {
      if (unit instanceof String text) {
        if (first && leadIn != null) {
          text = leadIn + text;
        }
        if (!bare) {
          lines.add("<para>");
        }
        lines.addAll(wrap(text));
        if (!bare) {
          lines.add("</para>");
        }
      } else {
        Node node = (Node) unit;
        if (first && leadIn != null) {
          lines.add("<para>" + leadIn.strip() + "</para>");
        }
        renderBlock(node, lines, allowBare && units.size() == 1);
      }
      first = false;
    }
  }

  private static void addParagraph(List<Object> units, List<Node> nodes) {
    String text = inline(nodes);
    if (!text.isEmpty()) {
      units.add(text);
    }
    nodes.clear();
  }

  private static void renderBlock(Node node, List<String> lines, boolean sole) {
    switch (node.name) {
      case "ul", "ol" -> {
        lines.add("<list type=\"" + (node.name.equals("ol") ? "number" : "bullet") + "\">");
        for (Node item : node.children) {
          if (item.isText() && item.text.isBlank()) {
            continue;
          }
          lines.add("<item>");
          lines.add("<description>");
          renderBlocks(
              item.name != null && item.name.equals("li") ? item.children : List.of(item),
              lines,
              null,
              true);
          lines.add("</description>");
          lines.add("</item>");
        }
        lines.add("</list>");
      }
      case "dl" -> {
        lines.add("<list type=\"bullet\">");
        boolean itemOpen = false;
        for (Node child : node.children) {
          if (child.isText() && child.text.isBlank()) {
            continue;
          }
          if ("dt".equals(child.name)) {
            if (itemOpen) {
              lines.add("</item>");
            }
            lines.add("<item>");
            lines.add("<term>" + inline(child.children) + "</term>");
            itemOpen = true;
          } else {
            if (!itemOpen) {
              lines.add("<item>");
            }
            lines.add("<description>");
            renderBlocks(
                "dd".equals(child.name) ? child.children : List.of(child), lines, null, true);
            lines.add("</description>");
            lines.add("</item>");
            itemOpen = false;
          }
        }
        if (itemOpen) {
          lines.add("</item>");
        }
        lines.add("</list>");
      }
      case "note", "important" -> {
        String label = node.name.equals("note") ? "Note" : "Important";
        renderBlocks(node.children, lines, "<b>" + label + ":</b> ", sole);
      }
      case "pre" -> {
        lines.add("<code>");
        String text = decodeAndEscape(rawText(node)).replace("\t", "    ");
        List<String> code = new ArrayList<>(List.of(text.split("\\R", -1)));
        while (!code.isEmpty() && code.get(0).isBlank()) {
          code.remove(0);
        }
        while (!code.isEmpty() && code.get(code.size() - 1).isBlank()) {
          code.remove(code.size() - 1);
        }
        for (String line : code) {
          lines.add(line.stripTrailing());
        }
        lines.add("</code>");
      }
      default -> renderBlocks(node.children, lines, null, sole);
    }
  }

  /** Inline content with whitespace collapsed, as the browser would show it. */
  private static String inline(List<Node> nodes) {
    StringBuilder sb = new StringBuilder();
    for (Node node : nodes) {
      appendInline(node, sb);
    }
    return WHITESPACE.matcher(sb).replaceAll(" ").strip();
  }

  private static void appendInline(Node node, StringBuilder sb) {
    if (node.isText()) {
      sb.append(decodeAndEscape(node.text));
      return;
    }
    switch (node.name) {
      case "code" -> wrapInline(node, sb, "c");
      case "b", "strong" -> wrapInline(node, sb, "b");
      case "i", "em", "replaceable" -> wrapInline(node, sb, "i");
      case "u" -> wrapInline(node, sb, "u");
      case "br" -> sb.append("<br/>");
      case "a" -> {
        if (node.text == null) {
          node.children.forEach(child -> appendInline(child, sb));
        } else {
          String href =
              decode(node.text)
                  .replace("&", "&amp;")
                  .replace("\"", "&quot;")
                  .replace("<", "&lt;")
                  .replace(">", "&gt;");
          String text = inline(node.children);
          if (text.isEmpty()) {
            sb.append("<see href=\"").append(href).append("\"/>");
          } else {
            sb.append("<see href=\"").append(href).append("\">").append(text).append("</see>");
          }
        }
      }
      // A block tag used inline: keep its text.
      default -> {
        sb.append(' ');
        node.children.forEach(child -> appendInline(child, sb));
        sb.append(' ');
      }
    }
  }

  private static void wrapInline(Node node, StringBuilder sb, String tag) {
    int start = sb.length();
    node.children.forEach(child -> appendInline(child, sb));
    if (sb.substring(start).isBlank()) {
      sb.setLength(start);
      return;
    }
    sb.insert(start, "<" + tag + ">").append("</").append(tag).append('>');
  }

  private static String rawText(Node node) {
    if (node.isText()) {
      return node.text;
    }
    StringBuilder sb = new StringBuilder();
    node.children.forEach(child -> sb.append(rawText(child)));
    return sb.toString();
  }

  private static String decodeAndEscape(String text) {
    return escape(decode(text));
  }

  private static String decode(String text) {
    if (text.indexOf('&') < 0) {
      return text;
    }
    Matcher m = ENTITY.matcher(text);
    StringBuilder sb = new StringBuilder();
    while (m.find()) {
      String entity = m.group(1);
      String replacement =
          switch (entity.toLowerCase(Locale.ROOT)) {
            case "amp" -> "&";
            case "lt" -> "<";
            case "gt" -> ">";
            case "quot" -> "\"";
            case "apos" -> "'";
            case "nbsp" -> " ";
            default -> {
              if (entity.startsWith("#")) {
                try {
                  int codePoint =
                      entity.charAt(1) == 'x' || entity.charAt(1) == 'X'
                          ? Integer.parseInt(entity.substring(2), 16)
                          : Integer.parseInt(entity.substring(1));
                  yield Character.isValidCodePoint(codePoint)
                      ? Character.toString(codePoint)
                      : m.group();
                } catch (NumberFormatException e) {
                  yield m.group();
                }
              }
              yield m.group();
            }
          };
      m.appendReplacement(sb, Matcher.quoteReplacement(replacement));
    }
    m.appendTail(sb);
    return sb.toString();
  }

  /** Greedy word wrap at spaces outside tags; the text has no newlines and single spaces. */
  private static List<String> wrap(String text) {
    List<String> lines = new ArrayList<>();
    StringBuilder line = new StringBuilder();
    for (String word : splitOutsideTags(text)) {
      if (line.length() > 0 && line.length() + 1 + word.length() > WIDTH) {
        lines.add(line.toString());
        line.setLength(0);
      }
      if (line.length() > 0) {
        line.append(' ');
      }
      line.append(word);
    }
    if (line.length() > 0) {
      lines.add(line.toString());
    }
    return lines;
  }

  private static List<String> splitOutsideTags(String text) {
    List<String> words = new ArrayList<>();
    StringBuilder word = new StringBuilder();
    boolean inTag = false;
    for (int i = 0; i < text.length(); i++) {
      char c = text.charAt(i);
      if (c == '<') {
        inTag = true;
      } else if (c == '>') {
        inTag = false;
      }
      if (c == ' ' && !inTag) {
        if (word.length() > 0) {
          words.add(word.toString());
          word.setLength(0);
        }
      } else {
        word.append(c);
      }
    }
    if (word.length() > 0) {
      words.add(word.toString());
    }
    return words;
  }
}
