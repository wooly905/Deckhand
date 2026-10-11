using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deckhand.Agent;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Deckhand.App;

public sealed record ChatMessageView(string Speaker, string Text)
{
    private bool IsUser => Speaker == "You";

    public bool IsMarkdown => Speaker == "Deckhand";

    public HorizontalAlignment Alignment => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public Thickness Margin => IsUser ? new Thickness(40, 0, 0, 10) : new Thickness(0, 0, 40, 10);

    public Brush Background => Resource(IsUser ? "AccentFillColorDefaultBrush"
        : Speaker == "Error" ? "SystemFillColorCriticalBackgroundBrush" : "DeckhandMessageBackgroundBrush");

    public Brush BorderBrush => Resource(Speaker == "Deckhand" ? "DeckhandMessageBorderBrush" : "CardStrokeColorDefaultBrush");

    public Brush Foreground => Resource(IsUser ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");

    private static Brush Resource(string key) => (Brush)Application.Current.Resources[key];
}

public partial class ChatViewModel : ObservableObject
{
    private readonly DesktopChatService service;

    public ChatViewModel(DesktopChatService service)
    {
        this.service = service;
        Input = "";
        Status = service.ModelLabel;
    }

    public ObservableCollection<ChatMessageView> Messages
    {
        get;
    } =
    [
        new("Deckhand",
            "Try an English request such as 'show disk usage', 'check my network', 'open Paint', or 'close Paint'. Deckhand offers Windows, file, browser, desktop, Paint, and script tools. Press Ctrl+Enter to send."),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Input
    {
        get; set;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewChatCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartNewRequestCommand))]
    public partial bool IsBusy
    {
        get; set;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartNewRequestCommand))]
    public partial bool AwaitingReply
    {
        get; set;
    }

    [ObservableProperty]
    public partial string Status
    {
        get; set;
    }

    public bool CanEdit => !IsBusy;

    private bool CanSend => CanEdit && !string.IsNullOrWhiteSpace(Input);

    private bool CanStartNewRequest => CanEdit && AwaitingReply;

    [RelayCommand(CanExecute = nameof(CanSend), IncludeCancelCommand = true)]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        var text = Input.Trim();
        Input = "";
        Messages.Add(new("You", text));
        IsBusy = true;
        Status = "Working...";
        try
        {
            var result = await service.RunAsync(text, cancellationToken);
            AwaitingReply = result.Status == RunStatus.AskedUser;
            Messages.Add(new(result.Status == RunStatus.Failed ? "Error" : "Deckhand", result.Text));
            Status = result.Status switch
            {
                RunStatus.AskedUser => "Waiting for your reply.",
                RunStatus.Cancelled => "Stopped. You can enter a new request.",
                RunStatus.Failed => "Could not complete the request. Check the model service in Settings.",
                _ => $"Done · {result.Steps} steps · {service.ModelLabel}",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AwaitingReply = false;
            Status = "Stopped. You can enter a new request.";
            Messages.Add(new("Deckhand", "Stopped."));
        }
        catch (Exception)
        {
            AwaitingReply = false;
            Messages.Add(new("Error", "Could not start the conversation. Check the model settings and try again."));
            Status = "Could not start the conversation.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void NewChat()
    {
        service.NewChat();
        Messages.Clear();
        Input = "";
        AwaitingReply = false;
        Status = service.ModelLabel;
    }

    [RelayCommand(CanExecute = nameof(CanStartNewRequest))]
    private void StartNewRequest()
    {
        service.StartNewRequest();
        AwaitingReply = false;
        Status = "Enter a new request.";
    }

    public void SettingsChanged()
    {
        NewChat();
        Messages.Add(new("Deckhand", "Model settings updated. A new conversation has started."));
    }
}
