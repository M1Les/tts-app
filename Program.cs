using System;
using System.Net.Http;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Text.RegularExpressions;

namespace tts_app
{
    class Program
    {
        private const string INPUT_PATH = "input";
        private const string OUTPUT_PATH = "output";

        private static readonly Regex mappingParser = new Regex(@"\([a-zA-Z\-]*\, ([a-zA-Z0-9\,\s]*)\)");
        static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Please, provide locale code as an argument.");
                return;
            }

            if (!Directory.Exists(INPUT_PATH))
            {
                Console.WriteLine("Input path not found");
                return;
            }

            if (!Directory.Exists(OUTPUT_PATH))
            {
                Directory.CreateDirectory(OUTPUT_PATH);
            }

            var textFiles = Directory.EnumerateFiles(INPUT_PATH, "*.txt", SearchOption.AllDirectories);

            Console.WriteLine($"Found {textFiles.Count()} files.");

            JArray langsArray = JArray.Parse(File.ReadAllText("languages.json"));
            var allLangs = langsArray.Select(
                l => new {
                    Locale = (string)l["locale"],
                    Language = (string)l["language"],
                    Gender = (string)l["gender"],
                    Mapping = (string)l["mapping"],
                });
            var langsForSelectedLocale = allLangs.Where(p => p.Locale.ToLower() == args[0].ToLower());

            var maleMapping = langsForSelectedLocale.FirstOrDefault(l => l.Gender.ToLower() == "male");
            var femaleMapping = langsForSelectedLocale.FirstOrDefault(l => l.Gender.ToLower() == "female");

            if (maleMapping == null)
            {
                Console.WriteLine("No male voice is available");
            }
            else
            {
                Console.WriteLine($"Male voice: {maleMapping.Mapping}");
            }

            if (femaleMapping == null)
            {
                Console.WriteLine("No female mapping is available");
            }
            else
            {
                Console.WriteLine($"Female voice: {femaleMapping.Mapping}");
            }

            // Prompts the user to input text for TTS conversion
            // Console.Write("What would you like to convert to speech? ");
            // string text = Console.ReadLine();

            // Gets an access token
            string accessToken;
            Console.WriteLine("Attempting token exchange. Please wait...\n");

            // Add your subscription key here
            // If your resource isn't in WEST US, change the endpoint
            Authentication auth = new Authentication("https://westus2.api.cognitive.microsoft.com/sts/v1.0/issueToken", "6e2af576376b4d3e94d9f222ca9ba383");
            try
            {
                accessToken = await auth.FetchTokenAsync().ConfigureAwait(false);
                Console.WriteLine("Successfully obtained an access token. \n");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to obtain an access token.");
                Console.WriteLine(ex.ToString());
                Console.WriteLine(ex.Message);
                return;
            }

            foreach(var inFilePath in textFiles)
            {
                try
                {
                    var fileText = File.ReadAllText(inFilePath);

                    if (maleMapping != null)
                    {
                        var outFilePath = GetOutFileName(inFilePath, GetShortNameFromMapping(maleMapping.Mapping), true);
                        await GenerateAudio(accessToken, "6e2af576376b4d3e94d9f222ca9ba383", fileText, maleMapping.Mapping, outFilePath);
                    }

                    if (femaleMapping != null)
                    {
                        var outFilePath = GetOutFileName(inFilePath, GetShortNameFromMapping(femaleMapping.Mapping));
                        await GenerateAudio(accessToken, "6e2af576376b4d3e94d9f222ca9ba383", fileText, femaleMapping.Mapping, outFilePath);
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"Error with file {inFilePath}. \n\n");
                    Console.WriteLine($"{ex.ToString()}. \n\n");
                }

            }

            Console.WriteLine("\nConversion is complete. Press any key to exit.");
            Console.ReadKey();
        }

        private static string GetOutFileName(string inFileName, string voiceName, bool male = false)
        {
            var fileName = Path.GetFileNameWithoutExtension(inFileName);
            var outFileName = Path.ChangeExtension($"vo_{fileName}_{voiceName.Replace(" ", "_").Replace(",", "_")}_{(male ? "m" : "f")}", "wav");
            return Path.Combine(OUTPUT_PATH, outFileName);
        }

        private static string GetShortNameFromMapping(string mappingFullName)
        {
            return mappingParser.Match(mappingFullName).Groups.Skip(1).FirstOrDefault()?.Value ?? string.Empty;
        }

        private static async Task GenerateAudio(string accessToken, string subscriptionKey, string text, string voiceName, string outFilePath)
        {
            string body = $@"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>
              <voice name='{voiceName}'>{text}</voice></speak>";

            using (var client = new HttpClient())
            {
                using (var request = new HttpRequestMessage())
                {
                    // Set the HTTP method
                    request.Method = HttpMethod.Post;
                    // Construct the URI
                    request.RequestUri = new Uri("https://westus2.tts.speech.microsoft.com/cognitiveservices/v1");
                    // Set the content type header
                    request.Content = new StringContent(body, Encoding.UTF8, "application/ssml+xml");
                    // Set additional header, such as Authorization and User-Agent
                    request.Headers.Add("Authorization", "Bearer " + accessToken);
                    request.Headers.Add("Connection", "Keep-Alive");
                    // Update your resource name
                    request.Headers.Add("User-Agent", "test-speech-getty");

                    request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
                    request.Headers.Add("X-Microsoft-OutputFormat", "riff-24khz-16bit-mono-pcm");
                    // Create a request
                    Console.WriteLine("Calling the TTS service. Please wait... \n");
                    using (var response = await client.SendAsync(request).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        // Asynchronously read the response
                        using (var dataStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        {
                            Console.WriteLine("Your speech file is being written to file...");
                            using (var fileStream = new FileStream(outFilePath, FileMode.Create, FileAccess.Write, FileShare.Write))
                            {
                                await dataStream.CopyToAsync(fileStream).ConfigureAwait(false);
                                fileStream.Close();
                            }
                            // Console.WriteLine("\nYour file is ready.");
                            // Console.ReadLine();
                        }
                    }
                }
            }
        }
    }
}
