using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using IdentityModel.Client;
namespace LinCms.Test
{
    public class UnitTest1
    {
        [Fact]
        public async Task ResourceOwnerPasswordAsync()
        {
            var dico = DiscoveryClient.GetAsync("http://localhost:5000").Result;

            //token
            var tokenClient = new TokenClient(dico.TokenEndpoint, "lin-cms-dotnetcore-client-id", "lin-cms-dotnetcore-client-secrets");
            var tokenResponse = await tokenClient.RequestResourceOwnerPasswordAsync("string", "string",extra:new Dictionary<string,string>
            {
                {"__tenant","123" }
            });
            if (tokenResponse.IsError)
            {
                return;
            }


            var httpClient = new HttpClient();
            httpClient.SetBearerToken(tokenResponse.AccessToken);

            var response = httpClient.GetAsync("http://localhost:5001/api/values").Result;
            if (!response.IsSuccessStatusCode)
            {
                string r = response.Content.ReadAsStringAsync().Result;
            }
        }

        [Fact]
        public async Task HttpClientResourePassword()
        {

        }
    }
}
