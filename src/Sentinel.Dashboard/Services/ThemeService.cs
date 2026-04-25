using Microsoft.JSInterop;

namespace Sentinel.Dashboard.Services
{
    public class ThemeService
    {
        private readonly IJSRuntime _js;
        private bool _isLightMode;

        public event Action? OnThemeChanged;

        public bool IsLightMode
        {
            get => _isLightMode;
            private set
            {
                if (_isLightMode != value)
                {
                    _isLightMode = value;
                    OnThemeChanged?.Invoke();
                }
            }
        }

        public ThemeService(IJSRuntime js)
        {
            _js = js;
        }

        public async Task ToggleThemeAsync()
        {
            IsLightMode = !IsLightMode;
            await ApplyThemeAsync();
            await SaveThemeAsync();
        }

        public async Task InitializeThemeAsync()
        {
            try
            {
                var storedTheme = await _js.InvokeAsync<string>("localStorage.getItem", "theme");
                IsLightMode = storedTheme == "light";
                await ApplyThemeAsync();
            }
            catch
            {
                // Silently fail if JS is not available yet (prerendering)
            }
        }

        private async Task ApplyThemeAsync()
        {
            try
            {
                await _js.InvokeVoidAsync("toggleBodyTheme", IsLightMode);
            }
            catch
            {
            }
        }

        private async Task SaveThemeAsync()
        {
            try
            {
                await _js.InvokeVoidAsync("localStorage.setItem", "theme", IsLightMode ? "light" : "dark");
            }
            catch
            {
            }
        }
    }
}
