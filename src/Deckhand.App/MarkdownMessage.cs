using System.Globalization;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Block = Markdig.Syntax.Block;

namespace Deckhand.App;

public sealed class MarkdownMessage : ContentControl
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables(new PipeTableOptions { UseGfmRules = true })
        .UseTaskLists()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text),
                                                                                         typeof(string),
                                                                                         typeof(MarkdownMessage),
                                                                                         new PropertyMetadata("",
                                                                                                              OnContentChanged));

    public static readonly DependencyProperty IsMarkdownProperty = DependencyProperty.Register(nameof(IsMarkdown),
                                                                                               typeof(bool),
                                                                                               typeof(MarkdownMessage),
                                                                                               new PropertyMetadata(true,
                                                                                                                    OnContentChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsMarkdown
    {
        get => (bool)GetValue(IsMarkdownProperty);
        set => SetValue(IsMarkdownProperty, value);
    }

    public MarkdownMessage() => Loaded += (_, _) => Render();

    private static void OnContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is MarkdownMessage { IsLoaded: true } message)
        {
            message.Render();
        }
    }

    private void Render()
    {
        if (!IsMarkdown)
        {
            Content = PlainText(Text);
            return;
        }

        Content = RenderBlocks(Markdown.Parse(Text ?? "", Pipeline));
    }

    private StackPanel RenderBlocks(ContainerBlock blocks)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var block in blocks)
        {
            panel.Children.Add(RenderBlock(block));
        }
        return panel;
    }

    private UIElement RenderBlock(Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var title = RenderText(heading);
                title.FontSize = FontSize + Math.Max(0, 10 - heading.Level * 2);
                title.FontWeight = FontWeights.SemiBold;
                return title;
            case ParagraphBlock paragraph:
                return RenderText(paragraph);
            case CodeBlock code:
                var text = PlainText(code.Lines.ToString());
                text.FontFamily = new FontFamily("Consolas");
                return new Border
                {
                    Background = Resource("CardBackgroundFillColorSecondaryBrush"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8),
                    Child = text,
                };
            case ListBlock list:
                return RenderList(list);
            case QuoteBlock quote:
                return new Border
                {
                    BorderBrush = Resource("CardStrokeColorDefaultBrush"),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(8, 0, 0, 0),
                    Child = RenderBlocks(quote),
                };
            case Table table:
                return RenderTable(table);
            case ThematicBreakBlock:
                return new Border
                {
                    Height = 1,
                    Background = Resource("DividerStrokeColorDefaultBrush"),
                    Margin = new Thickness(0,
                                           4,
                                           0,
                                           4)
                };
            case ContainerBlock container:
                return RenderBlocks(container);
            case LeafBlock leaf:
                return PlainText(leaf.Lines.ToString());
            default:
                return PlainText("");
        }
    }

    private StackPanel RenderList(ListBlock list)
    {
        var panel = new StackPanel { Spacing = 4 };
        var number = int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ? start : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var marker = PlainText(list.IsOrdered ? $"{number++}." : "•");
            marker.MinWidth = 14;
            row.Children.Add(marker);
            var content = RenderBlocks(item);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            panel.Children.Add(row);
        }
        return panel;
    }

    private ScrollViewer RenderTable(Table table)
    {
        var grid = new Grid();
        for (var column = 0; column < table.ColumnDefinitions.Count; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        for (var rowIndex = 0; rowIndex < table.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = (TableRow)table[rowIndex];
            for (var column = 0; column < row.Count; column++)
            {
                var content = RenderBlocks((TableCell)row[column]);
                foreach (var text in content.Children.OfType<RichTextBlock>())
                {
                    text.FontWeight = row.IsHeader ? FontWeights.SemiBold : FontWeights.Normal;
                    text.TextAlignment = table.ColumnDefinitions[column].Alignment switch
                    {
                        TableColumnAlign.Center => TextAlignment.Center,
                        TableColumnAlign.Right => TextAlignment.Right,
                        _ => TextAlignment.Left,
                    };
                }
                var cell = new Border
                {
                    BorderBrush = Resource("CardStrokeColorDefaultBrush"),
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(6),
                    Child = content,
                    Background = row.IsHeader ? Resource("CardBackgroundFillColorSecondaryBrush") : null,
                };
                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }
        return new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };
    }

    private RichTextBlock PlainText(string text)
    {
        var block = NewTextBlock();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = text });
        block.Blocks.Add(paragraph);
        return block;
    }

    private RichTextBlock NewTextBlock() => new()
    {
        FontSize = FontSize,
        Foreground = Foreground,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
    };

    private RichTextBlock RenderText(LeafBlock block)
    {
        var text = NewTextBlock();
        var paragraph = new Paragraph();
        if (block.Inline is not null)
        {
            AppendInlines(paragraph.Inlines, block.Inline);
        }
        text.Blocks.Add(paragraph);
        return text;
    }

    private void AppendInlines(InlineCollection target, ContainerInline container)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(new Run { Text = literal.Content.ToString() });
                    break;
                case CodeInline code:
                    target.Add(new Run { Text = code.Content, FontFamily = new FontFamily("Consolas") });
                    break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterChar == '~')
                    {
                        span.TextDecorations = global::Windows.UI.Text.TextDecorations.Strikethrough;
                    }
                    else if (emphasis.DelimiterCount >= 2)
                    {
                        span.FontWeight = FontWeights.Bold;
                    }
                    else
                    {
                        span.FontStyle = global::Windows.UI.Text.FontStyle.Italic;
                    }

                    AppendInlines(span.Inlines, emphasis);
                    target.Add(span);
                    break;
                case LinkInline link:
                    var linkSpan = LinkSpan(link.Url);
                    AppendInlines(linkSpan.Inlines, link);
                    target.Add(linkSpan);
                    // Images remain a label and link; displaying a reply must not fetch remote resources.
                    if (link.IsImage)
                    {
                        target.Add(new Run { Text = $" ({link.Url})" });
                    }

                    break;
                case AutolinkInline link:
                    var autoLink = LinkSpan(link.Url);
                    autoLink.Inlines.Add(new Run { Text = link.Url });
                    target.Add(autoLink);
                    break;
                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;
                case HtmlEntityInline entity:
                    target.Add(new Run { Text = entity.Transcoded.ToString() });
                    break;
                case TaskList task:
                    target.Add(new Run { Text = task.Checked ? "☑ " : "☐ " });
                    break;
                case ContainerInline nested:
                    AppendInlines(target, nested);
                    break;
            }
        }
    }

    private static Span LinkSpan(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
        ? new Hyperlink { NavigateUri = uri }
        : new Span();

    private static Brush Resource(string key) => (Brush)Application.Current.Resources[key];
}
