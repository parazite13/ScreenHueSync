using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Q42.HueApi;
using Q42.HueApi.Models.Bridge;
using Q42.HueApi.Streaming;
using Q42.HueApi.Streaming.Models;

namespace ScreenHueSync
{
    public class Hue
    {
        public static bool Ready { get; private set; } = false;
        public static StreamingHueClient Client { get; private set; }
        public static StreamingGroup StreamingGroup { get; private set; }
        public static EntertainmentLayer BaseLayer { get; private set; }

        private static HueCredential hueCredential;

        private const string credentialPath = "hue_credentials.json";

        public static async Task Setup(CancellationToken token)
        {
            var bridges = await HueBridgeDiscovery.FastDiscoveryWithNetworkScanFallbackAsync(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
            var bridge = bridges.FirstOrDefault();

            RegisterEntertainmentResult registeredInfos;

            // Is the Hue credentials present ?
            if (!File.Exists(credentialPath))
            {
                Console.WriteLine("No credentials found please press the bridge button");

                // Wait for the user to press the link button
                await Task.Delay(TimeSpan.FromSeconds(30));

                var client = new LocalHueClient(bridge.IpAddress);
                registeredInfos = await client.RegisterAsync("ScreenHueSync", Environment.MachineName, true);
                hueCredential = new HueCredential()
                {
                    Username = registeredInfos.Username,
                    Key = registeredInfos.StreamingClientKey
                };
                File.WriteAllText(credentialPath, Newtonsoft.Json.JsonConvert.SerializeObject(hueCredential));
                Console.WriteLine("Registration success credentials are :");
                Console.WriteLine("Username : " + registeredInfos.Username);
                Console.WriteLine("Key : " + registeredInfos.StreamingClientKey);
            }
            else
            {
                hueCredential = Newtonsoft.Json.JsonConvert.DeserializeObject<HueCredential>(File.ReadAllText(credentialPath));
            }

            registeredInfos = new RegisterEntertainmentResult()
            {
                Username = hueCredential.Username,
                StreamingClientKey = hueCredential.Key
            };

            Console.WriteLine("Get client");
            Client = new StreamingHueClient(bridge.IpAddress, registeredInfos.Username, registeredInfos.StreamingClientKey);

            //Get the entertainment group
            Console.WriteLine("Get entertainment group");
            var all = await Client.LocalHueClient.GetEntertainmentGroups();
            var group = all.Last();

            //Create a streaming group
            Console.WriteLine("Get streaming group");
            StreamingGroup = new StreamingGroup(group.Locations);

            //Connect to the streaming group
            Console.WriteLine("Connect to group");
            await Client.Connect(group.Id);

            Console.WriteLine("Done !");
            BaseLayer = StreamingGroup.GetNewLayer(true);
            Ready = true;

            //Start auto updating this entertainment group
            _ = Client.AutoUpdate(StreamingGroup, token, 50);

        }

        public static void Dispose()
        {
            Client.Close();
        }

        [Serializable]
        private struct HueCredential
        {
            public string Username;
            public string Key;
        }
    }
}