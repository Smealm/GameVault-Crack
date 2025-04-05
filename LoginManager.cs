using gamevault.Models;
using gamevault.ViewModels;
using IdentityModel.Client;
using IdentityModel.OidcClient;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Documents;
using System.Windows.Threading;
using Windows.Media.Protection.PlayReady;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace gamevault.Helper
{
    public enum LoginState
    {
        Success,
        Error,
        Unauthorized,
        Forbidden
    }
    internal class LoginManager
    {
        #region Singleton
        private static LoginManager instance = null;
        private static readonly object padlock = new object();

        public static LoginManager Instance
        {
            get
            {
                lock (padlock)
                {
                    if (instance == null)
                    {
                        instance = new LoginManager();
                    }
                    return instance;
                }
            }
        }
        #endregion
        private User? m_User { get; set; }
        private LoginState m_LoginState { get; set; }
        private string m_LoginMessage { get; set; }
        private Timer onlineTimer { get; set; }
        public User? GetCurrentUser()
        {
            return m_User;
        }
        public bool IsLoggedIn()
        {
            return m_User != null;
        }
        public LoginState GetState()
        {
            return m_LoginState;
        }
        public string GetLoginMessage()
        {
            return m_LoginMessage;
        }
        public void SwitchToOfflineMode()
        {
            MainWindowViewModel.Instance.OnlineState = System.Windows.Visibility.Visible;
            m_User = null;
        }
        public async Task StartupLogin()
        {
            LoginState state = LoginState.Success;
            if (IsLoggedIn()) return;
            User? user = await Task<User>.Run(() =>
            {
                try
                {
                    WebHelper.SetCredentials(Preferences.Get(AppConfigKey.Username, AppFilePath.UserFile), Preferences.Get(AppConfigKey.Password, AppFilePath.UserFile, true));
                    string result = WebHelper.GetRequest(@$"{SettingsViewModel.Instance.ServerUrl}/api/users/me", 5000);
                    return JsonSerializer.Deserialize<User>(result);
                }
                catch (Exception ex)
                {
                    string code = WebExceptionHelper.GetServerStatusCode(ex);
                    state = DetermineLoginState(code);
                    if (state == LoginState.Error)
                        m_LoginMessage = WebExceptionHelper.TryGetServerMessage(ex);

                    return null;
                }
            });
            m_User = user;
            m_LoginState = state;
            InitOnlineTimer();
        }
        public async Task<LoginState> ManualLogin(string username, string password)
        {
            LoginState state = LoginState.Success;
            User? user = await Task<User>.Run(() =>
            {
                try
                {
                    WebHelper.OverrideCredentials(username, password);
                    string result = WebHelper.GetRequest(@$"{SettingsViewModel.Instance.ServerUrl}/api/users/me");
                    return JsonSerializer.Deserialize<User>(result);
                }
                catch (Exception ex)
                {
                    string code = WebExceptionHelper.GetServerStatusCode(ex);
                    state = DetermineLoginState(code);
                    if (state == LoginState.Error)
                        m_LoginMessage = WebExceptionHelper.TryGetServerMessage(ex);

                    return null;
                }
            });
            m_User = user;
            m_LoginState = state;
            return state;
        }
        public void Logout()
        {
            m_User = null;
            m_LoginState = LoginState.Error;
            WebHelper.OverrideCredentials(string.Empty, string.Empty);
            MainWindowViewModel.Instance.Community.Reset();
        }
        private WpfEmbeddedBrowser wpfEmbeddedBrowser = null;
        public async Task PhalcodeLogin(bool startHidden = false)
        {
            DateTime expiry = DateTime.Parse("1/1/9999 12:00");
            string username = "Admin";
            string provider = "phalcode";
            Preferences.Set(AppConfigKey.Phalcode1, provider, AppFilePath.UserFile, useEncryption: true);
            SettingsViewModel.Instance.License = new PhalcodeProduct
            {
                UserName = username,
                CurrentPeriodEnd = expiry
            };
            Preferences.Set(AppConfigKey.Phalcode2, JsonSerializer.Serialize(SettingsViewModel.Instance.License), AppFilePath.UserFile, useEncryption: true);
        }
        public void PhalcodeLogout()
        {
            SettingsViewModel.Instance.License = new PhalcodeProduct();
            Preferences.DeleteKey(AppConfigKey.Phalcode1.ToString(), AppFilePath.UserFile);
            Preferences.DeleteKey(AppConfigKey.Phalcode2.ToString(), AppFilePath.UserFile);
            Preferences.DeleteKey(AppConfigKey.Theme, AppFilePath.UserFile);
            try
            {
                Directory.Delete(AppFilePath.WebConfigDir, true);
                //wpfEmbeddedBrowser.ClearAllCookies();
            }
            catch (Exception ex) { }
        }
        private LoginState DetermineLoginState(string code)
        {
            switch (code)
            {
                case "401":
                    {
                        return LoginState.Unauthorized;
                    }
                case "403":
                    {
                        return LoginState.Forbidden;
                    }
            }
            return LoginState.Error;
        }
        private void InitOnlineTimer()
        {
            if (onlineTimer == null)
            {
                onlineTimer = new Timer(30000);//30 Seconds
                onlineTimer.AutoReset = true;
                onlineTimer.Elapsed += CheckOnlineStatus;
                onlineTimer.Start();
            }
        }
        private async void CheckOnlineStatus(object sender, EventArgs e)
        {
            try
            {
                string serverResonse = await WebHelper.GetRequestAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/health");
                if (!IsLoggedIn())
                {
                    await StartupLogin();
                    if (IsLoggedIn())
                    {
                        MainWindowViewModel.Instance.OnlineState = System.Windows.Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                SwitchToOfflineMode();
            }
        }
    }
}
