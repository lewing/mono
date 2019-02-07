using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using System.Net.WebSockets;
using System.Net.Http;
using System.Threading;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using System.Net;

namespace WsProxy {

	internal class Startup {
		// This method gets called by the runtime. Use this method to add services to the container.
		// For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
		public void ConfigureServices (IServiceCollection services)
		{
			services.AddRouting ();
		}

		public static async Task ProxyMsg (string desc, WebSocket from, WebSocket to)
		{
			byte [] buff = new byte [4000];
			var mem = new MemoryStream ();
			while (true) {
				var result = await from.ReceiveAsync (new ArraySegment<byte> (buff), CancellationToken.None);
				if (result.MessageType == WebSocketMessageType.Close) {
					await to.SendAsync (new ArraySegment<byte> (mem.GetBuffer (), 0, (int)mem.Length), WebSocketMessageType.Close, true, CancellationToken.None);
					return;
				}

				if (result.EndOfMessage) {
					mem.Write (buff, 0, result.Count);

					var str = Encoding.UTF8.GetString (mem.GetBuffer (), 0, (int)mem.Length);

					await to.SendAsync (new ArraySegment<byte> (mem.GetBuffer (), 0, (int)mem.Length), WebSocketMessageType.Text, true, CancellationToken.None);
					mem.SetLength (0);
				} else {
					mem.Write (buff, 0, result.Count);
				}
			}
		}

		Uri GetBrowserUri (string path)
		{
			return new Uri ("ws://localhost:9222" + path);
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public void Configure (IApplicationBuilder app, IHostingEnvironment env)
		{
			//loggerFactory.AddConsole();
			//loggerFactory.AddDebug();
			app.UseDeveloperExceptionPage ()
				.UseWebSockets ()
				.UseMonoDebuggerProxy ()
				.UseRouter (router => {
					router.MapGet ("devtools/page/{pageId}", async context => {
						if (!context.WebSockets.IsWebSocketRequest) {
							context.Response.StatusCode = 400;
							return;
						}

						try {
							var proxy = new MonoProxy ();
							var browserUri = GetBrowserUri (context.Request.Path.ToString ());
							var ideSocket = await context.WebSockets.AcceptWebSocketAsync ();

							await proxy.Run (browserUri, ideSocket);
						} catch (Exception e) {
							Console.WriteLine ("got exception {0}", e);
						}
					});
				});
		}
	}

	class BrowserTab {
		public string description { get; set; }
		public string id { get; set; }
		public string type { get; set; }
		public string url { get; set; }
		public string title { get; set; }
		public string devtoolsFrontendUrl { get; set; }
		public string webSocketDebuggerUrl { get; set; }
		public string faviconUrl { get; set; }
	}

	static class MonoProxyExtensions {
		public static BrowserTab MonoProxyTabRewriter (BrowserTab tab, HttpContext context, Uri debuggerHost)
		{
			var request = context.Request;
			var frontendUrl = $"{debuggerHost.Scheme}://{debuggerHost.Authority}{tab.devtoolsFrontendUrl.Replace ($"ws={debuggerHost.Authority}", $"ws={request.Host}")}";
			var wsUrl = new Uri (tab.webSocketDebuggerUrl);
			var proxyDebuggerUrl = $"{wsUrl.Scheme}://{request.Host}{wsUrl.PathAndQuery}";
			
			return new BrowserTab {
				description = tab.description,
				devtoolsFrontendUrl = frontendUrl,
				id = tab.id,
				title = tab.title,
				type = tab.type,
				url = tab.url,
				webSocketDebuggerUrl = proxyDebuggerUrl,
				faviconUrl = tab.faviconUrl
			};
		}

		public static IApplicationBuilder UseMonoDebuggerProxy (this IApplicationBuilder app)
		{
			return UseMonoDebuggerProxy (app, MonoProxyTabRewriter);
		}

		public static IApplicationBuilder UseMonoDebuggerProxy (this IApplicationBuilder app, Func<BrowserTab, HttpContext, Uri, BrowserTab> rewriteFunc)
		{
			app.Use(async (context, next) => {
				var debuggerHost = new Uri ("http://localhost:9222");
				var request = context.Request;

				var requestPath = request.Path;
				var endpoint = $"{debuggerHost.Scheme}://{debuggerHost.Authority}{request.Path}{request.QueryString}";
				
				switch (requestPath.Value.ToLower (System.Globalization.CultureInfo.InvariantCulture)) {
					case "/":
						using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds (5) }) {
							var response = await httpClient.GetStringAsync ($"{debuggerHost}");
							context.Response.ContentType = "text/html";
							context.Response.ContentLength = response.Length;
							await context.Response.WriteAsync (response);
						}
						break;
					case "/json/version":
						context.Response.ContentType = "application/json";
						await context.Response.WriteAsync (JsonConvert.SerializeObject (
						new Dictionary<string, string> {
							{ "Browser", "Chrome/71.0.3578.98" },
							{ "Protocol-Version", "1.3" },
							{ "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/71.0.3578.98 Safari/537.36" },
							{ "V8-Version", "7.1.302.31" },
							{ "WebKit-Version", "537.36 (@15234034d19b85dcd9a03b164ae89d04145d8368)" },
						}));
						break;
					case "/json/new":
						var tab = await ProxyGetAsync<BrowserTab> (endpoint);
						var redirect = rewriteFunc?.Invoke (tab, context, debuggerHost);
						context.Response.ContentType = "application/json";
						await context.Response.WriteAsync (JsonConvert.SerializeObject (redirect));
						break;
					case "/json/list":
					case "/json":
						var tabs = await ProxyGetAsync<BrowserTab[]>(endpoint);
						var alteredTabs = tabs.Select (t => rewriteFunc?.Invoke (t, context, debuggerHost)).ToArray();
						context.Response.ContentType = "application/json";
						await context.Response.WriteAsync (JsonConvert.SerializeObject (alteredTabs));
						break;
					default:
						await next();
						break;
				}
			});
			return app;
		}

		public static async Task<T> ProxyGetAsync<T>(string url)
		{
			using (var httpClient = new HttpClient ()) {
				var response = await httpClient.GetAsync (url);
				var jsonResponse = await response.Content.ReadAsStringAsync ();
				return JsonConvert.DeserializeObject<T> (jsonResponse);
			}
		}
	}
}
