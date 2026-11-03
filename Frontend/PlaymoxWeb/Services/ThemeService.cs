using Microsoft.JSInterop;

namespace PlaymoxWeb.Services
{
    public class ThemeService
    {
        private readonly IJSRuntime _jsRuntime;
        private const string ThemeKey = "playmox_theme";
        
        public bool IsDarkMode { get; set; } = true;

        public event Action? OnThemeChanged;

        public ThemeService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task InitializeAsync()
        {
            var storedTheme = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", ThemeKey);
            if (!string.IsNullOrEmpty(storedTheme))
            {
                IsDarkMode = storedTheme == "dark";
            }
            else
            {
                // Default to dark
                IsDarkMode = true;
            }
            OnThemeChanged?.Invoke();
        }

        public async Task ToggleThemeAsync()
        {
            IsDarkMode = !IsDarkMode;
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", ThemeKey, IsDarkMode ? "dark" : "light");
            OnThemeChanged?.Invoke();
        }
    }
}
