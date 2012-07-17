﻿using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VkToolkit.Categories;
using VkToolkit.Enum;
using VkToolkit.Exception;
using VkToolkit.Utils;

namespace VkToolkit
{
    public class VkApi
    {
        public string Email { get; set; }
        public string Password { get; set; }

        public ResponseType ResponseType { get; set; }
        public int AppId { get; private set; }
        public string SecureKey { get; private set; }
        public string AccessToken { get; internal set; }
        public string ExpiresIn { get; private set; }   // todo string -> int
        public int UserId { get; private set; }      // todo string -> int

        public Users Users { get; private set; }
        public Friends Friends { get; private set; }
        public StatusCategory Status { get; private set; }
        public MessagesCategory Messages { get; private set; }
        public GroupsCategory Groups { get; private set; }

        private const string MethodPrefix = "https://api.vk.com/method/";
        internal static string InvalidLoginOrPassword = "Invalid login or password";
        internal static string LoginSuccessed = "Login success";

        internal IBrowser Browser;

        public VkApi(IBrowser browser = null)
        {
            // set default values
            ResponseType = ResponseType.Json;

            Browser = browser ?? new Utils.Browser(); // undone add test
            
            // set function's categories
            Users = new Users(this);
            Friends = new Friends(this);
            Status = new StatusCategory(this);
            Messages = new MessagesCategory(this);
            Groups = new GroupsCategory(this);
        }

        /// <summary>
        /// Authorize application on vk.com and getting Access Token.
        /// </summary>
        /// <param name="appId">Appliation Id</param>
        /// <param name="email">Email or Phone</param>
        /// <param name="password">Password</param>
        /// <param name="settings">Access rights requested by your application</param>
        /// <param name="display">Type of output page</param>
        [STAThread]
        public void Authorize(int appId, string email, string password, Settings settings, Display display)
        {
            Email = email;
            Password = password;
            AppId = appId;

            string url = CreateAuthorizeUrl(appId, settings, display);

            Browser.ClearCookies();
            Browser.GoTo(url);
            try
            {
                Browser.Authorize(email, password);
            }
            catch(VkApiException)
            {
                Browser.Close();
                throw;
            }

            if (Browser.ContainsText(InvalidLoginOrPassword))
            {
                Browser.Close();
                throw new VkApiAuthorizationException(InvalidLoginOrPassword, email, password);
            }

            // we run our application at first time
            // we need gain access
            if (!Browser.ContainsText(LoginSuccessed))
                Browser.GainAccess();

            if (!Browser.ContainsText(LoginSuccessed))
            {
                Browser.Close();
                throw new VkApiException();
            }

            // parse values from url
            Uri successUrl = Browser.Uri;
            string[] parts = successUrl.Fragment.Split('&');

            // todo IndexOutOfRangeException
            AccessToken = parts[0].Split('=')[1];
            ExpiresIn = parts[1].Split('=')[1];
            try
            {
                UserId = Convert.ToInt32(parts[2].Split('=')[1]);
            }
            catch (FormatException ex)
            {
                UserId = -1;
                throw new VkApiException("UserId is not integer value.", ex);
            }
            finally
            {
                Browser.Close();    
            }
        }

        public string GetApiUrl(string method, IDictionary<string, string> values)
        {
            var sb = new StringBuilder();
            sb.AppendFormat("{0}{1}", MethodPrefix, method);
            if (ResponseType == ResponseType.Xml)
                sb.Append(".xml");
            sb.Append("?");

            foreach (var kvp in values)
            {
                sb.AppendFormat("{0}={1}&", kvp.Key, kvp.Value);
            }

            sb.AppendFormat("access_token={0}", AccessToken);
            
            return sb.ToString();
        }

        internal string CreateAuthorizeUrl(int appId, Enum.Settings settings, Display display)
        {
            var sb = new StringBuilder("http://oauth.vk.com/authorize?");
            sb.AppendFormat("client_id={0}&", appId);
            sb.AppendFormat("scope={0}&", settings);
            sb.Append("redirect_uri=http://oauth.vk.com/blank.html&");
            sb.AppendFormat("display={0}&", display);
            sb.Append("response_type=token");

            return sb.ToString();
        }

        #region Private & Internal Methods
        internal void IfAccessTokenNotDefinedThrowException()
        {
            if (string.IsNullOrEmpty(AccessToken))
                throw new AccessTokenInvalidException();
        }

        internal void IfErrorThrowException(string json)
        {
            if (string.CompareOrdinal(json.Substring(2, 5), "error") != 0) return;

            JObject obj;
            try
            {
                obj = JObject.Parse(json);
            }
            catch (JsonReaderException ex)
            {
                throw new VkApiException("Wrong json data.", ex);
            }

            var response = obj["error"];

            var code = (int)response["error_code"];
            var message = (string)response["error_msg"];

            switch (code)
            {
                case 5:
                    throw new UserAuthorizationFailException(message, code);

                case 113:   // Invalid user id.
                case 125:
                    throw new InvalidParamException(message, code);

                case 7:     // Permission to perform this action is denied by user.
                case 15:
                case 221:
                case 203:   // Access to the group is denied.
                case 260:   // Access to the groups list is denied due to the user's privacy settings.
                case 500:
                    throw new AccessDeniedException(message, code);

                default:
                    throw new VkApiException(message);
            }
        }
        #endregion
    }
}
