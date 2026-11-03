namespace PlaymoxWeb.Services;

public sealed class TutorialService
{
    private bool _pendingInstancesTutorial;

    public event Action? InstancesTutorialRequested;

    public void RequestInstancesTutorial()
    {
        _pendingInstancesTutorial = true;
        InstancesTutorialRequested?.Invoke();
    }

    public bool ConsumePendingInstancesTutorial()
    {
        if (!_pendingInstancesTutorial)
        {
            return false;
        }

        _pendingInstancesTutorial = false;
        return true;
    }
}
