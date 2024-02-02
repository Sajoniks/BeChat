namespace BeChat.Client.View;

public abstract class View
{
    protected Window Parent { get; }
    
    public View(Window w)
    {
        Parent = w;
    }

    public bool IsVisible => Parent.IsVisible(this);
    
    public abstract void OnShow();
    public abstract void OnClose();
    public abstract bool OnKeyboardInput(ConsoleKeyInfo keyInfo);
    public abstract bool OnKeyboardCancel();
    public abstract IEnumerable<View> Views { get; }
}