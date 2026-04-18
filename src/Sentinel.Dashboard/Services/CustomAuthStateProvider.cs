using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Security.Claims;

namespace Sentinel.Dashboard.Services;

public class CustomAuthStateProvider : AuthenticationStateProvider
{
    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly ClaimsPrincipal _anonymous = new(new ClaimsIdentity());

    public CustomAuthStateProvider(ProtectedSessionStorage sessionStorage)
    {
        _sessionStorage = sessionStorage;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var sessionTask = _sessionStorage.GetAsync<bool>("IsAuthenticated");
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5)); // Aumentado para mayor estabilidad en demos

            var completedTask = await Task.WhenAny(sessionTask.AsTask(), timeoutTask);

            if (completedTask == timeoutTask)
            {
                System.Console.WriteLine("[AUTH] ⚠ Timeout (5s) alcanzado recuperando sesión. Continuando como guest.");
                return new AuthenticationState(_anonymous);
            }

            var userSessionResult = await sessionTask;
            var isAuthenticated = userSessionResult.Success ? userSessionResult.Value : false;

            if (isAuthenticated)
            {
                var claims = new List<Claim> { new Claim(ClaimTypes.Name, "Admin") };
                var identity = new ClaimsIdentity(claims, "CustomAuth");
                var user = new ClaimsPrincipal(identity);
                return new AuthenticationState(user);
            }
        }
        catch (OperationCanceledException)
        {
            System.Console.WriteLine("[AUTH-PROVIDER] Timeout alcanzado al recuperar sesión. Continuando como anónimo.");
        }
        catch (Exception ex)
        {
            // Falla si JavaScript no está disponible (pre-rendering) o la sesión está corrupta
            // Simplemente devolvemos anónimo de forma segura
            System.Console.WriteLine($"[AUTH-PROVIDER] Error recuperando sesión: {ex.Message}");
        }

        return new AuthenticationState(_anonymous);
    }

    public async Task MarkUserAsAuthenticated()
    {
        await _sessionStorage.SetAsync("IsAuthenticated", true);
        var claims = new List<Claim> { new Claim(ClaimTypes.Name, "Admin") };
        var identity = new ClaimsIdentity(claims, "CustomAuth");
        var user = new ClaimsPrincipal(identity);

        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
    }

    public async Task MarkUserAsLoggedOut()
    {
        await _sessionStorage.DeleteAsync("IsAuthenticated");
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_anonymous)));
    }
}
