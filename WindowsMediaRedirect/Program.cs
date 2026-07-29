using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace WindowsMediaRedirect {
    static class Program {
        public static HttpListener listener;
        public static string ip = "127.0.0.1";
        public static int requestCount = 0;

        // Variabili globali per salvare l'artista e l'album correnti (compatibile C# 2.0)
        public static string currentArtist = "";
        public static string currentAlbum = "";

        public static string[] hosts = new string[] {
            "redir.metaservices.microsoft.com",
            "images.metaservices.microsoft.com",
            "toc.music.metaservices.microsoft.com",
            "windowsmedia.com",
            "www.windowsmedia.com",
            "services.windowsmedia.com"
        };

        public static void HandleIncomingConnections() {
            while (true) {
                HttpListenerContext ctx = listener.GetContext();

                HttpListenerRequest req = ctx.Request;
                HttpListenerResponse resp = ctx.Response;

                Console.WriteLine("Request #: {0}", ++requestCount);
                Console.WriteLine(req.Url.ToString());
                Console.WriteLine(req.HttpMethod);
                Console.WriteLine(req.UserHostName);
                Console.WriteLine(req.UserAgent);
                Console.WriteLine();

                string target = "http://musicmatch-ssl.xboxlive.com/cdinfo/GetMDRCD.aspx" + req.Url.Query;

                if (req.Url.ToString().StartsWith("http://windowsmedia.com/redir/GetMDRCD.asp") || req.Url.ToString().StartsWith("http://windowsmedia.com/redir/QueryTOC.asp")) {
                    WebClient wc = new WebClient();
                    wc.Encoding = System.Text.Encoding.UTF8;
                    XmlSerializer newSerializer = new XmlSerializer(typeof(NewMetadata.METADATA));
                    XmlSerializer oldSerializer = new XmlSerializer(typeof(OldMetadata.METADATA));

                    string xmlin;
                    byte[] data;

                    try {
                        xmlin = wc.DownloadString(target);
                        StringReader reader = new StringReader(xmlin);
                        NewMetadata.METADATA newmeta = (NewMetadata.METADATA)newSerializer.Deserialize(reader);

                        // Salviamo Artista e Album per poterli usare se il download della copertina da Microsoft fallisce
                        if (newmeta != null && newmeta.MDRCD != null) {
                            currentArtist = newmeta.MDRCD.AlbumArtist;
                            currentAlbum = newmeta.MDRCD.AlbumTitle;
                            Console.WriteLine("Trovato album: " + currentArtist + " - " + currentAlbum);
                        }

                        StringWriter swriter = new StringWriter();
                        oldSerializer.Serialize(XmlWriter.Create(swriter), NewToOldMeta(newmeta));
                        data = Encoding.UTF8.GetBytes(swriter.ToString());
                    } catch (Exception ex) {
                        Console.WriteLine(ex.ToString());
                        resp.StatusCode = 500;
                        resp.Close();
                        return;
                    }

                    resp.ContentType = "text/xml";
                    resp.ContentEncoding = Encoding.UTF8;
                    resp.ContentLength64 = data.LongLength;

                    resp.OutputStream.Write(data, 0, data.Length);
                } else if (req.Url.ToString().StartsWith("http://services.windowsmedia.com/cover/")) {
                    string imgurl = req.Url.GetLeftPart(UriPartial.Path).Replace("http://services.windowsmedia.com/cover/", "http://musicimage.xboxlive.com/");
                    WebClient wc = new WebClient();
                    byte[] data = null;

                    // 1. Tenta prima il download dal server ufficiale Microsoft
                    try {
                        data = wc.DownloadData(imgurl);
                    } catch {
                        Console.WriteLine("Server Microsoft fallito per l'immagine. Tentativo di fallback su iTunes...");
                        
                        // 2. Fallback su iTunes se il server Microsoft fallisce
                        if (!string.IsNullOrEmpty(currentArtist) && !string.IsNullOrEmpty(currentAlbum)) {
                            string iTunesUrl = GetiTunesCoverUrl(currentArtist, currentAlbum);
                            if (!string.IsNullOrEmpty(iTunesUrl)) {
                                try {
                                    data = wc.DownloadData(iTunesUrl);
                                    Console.WriteLine("Copertina scaricata con successo da iTunes!");
                                } catch (Exception ex) {
                                    Console.WriteLine("Errore download copertina da iTunes: " + ex.Message);
                                }
                            }
                        }
                    }

                    if (data != null) {
                        resp.ContentType = "image/jpeg";
                        resp.ContentLength64 = data.LongLength;
                        resp.OutputStream.Write(data, 0, data.Length);
                    } else {
                        resp.StatusCode = 404;
                    }
                } else if (req.Url.ToString().StartsWith("http://images.metaservices.microsoft.com/cover/")) {
                    string imgurl = req.Url.GetLeftPart(UriPartial.Path).Replace("http://images.metaservices.microsoft.com/cover/https:/musicimage.xboxlive.com/", "http://musicimage.xboxlive.com/");
                    resp.Redirect(imgurl);
                } else {
                    resp.Redirect(target);
                }
                resp.Close();
            }
        }

        // Funzione di ricerca su iTunes (100% C# 2.0 / .NET 2.0)
        static string GetiTunesCoverUrl(string artist, string album) {
            try {
                WebClient wc = new WebClient();
                wc.Encoding = Encoding.UTF8;

                // Tenta di abilitare TLS 1.2 se supportato dal sistema operativo
                try {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                } catch { }

                string query = Uri.EscapeDataString(artist + " " + album);
                string json = wc.DownloadString("https://itunes.apple.com/search?term=" + query + "&entity=album&limit=1");

                // Estrazione manuale della stringa (Senza JSON parser esterni)
                int index = json.IndexOf("\"artworkUrl100\":\"");
                if (index != -1) {
                    index += 17;
                    int end = json.IndexOf("\"", index);
                    string url = json.Substring(index, end - index);
                    
                    // Sostituisce 100x100 con 600x600 per copertine ad alta definizione
                    return url.Replace("100x100bb.jpg", "600x600bb.jpg");
                }
            } catch (Exception ex) {
                Console.WriteLine("Ricerca iTunes fallita: " + ex.Message);
            }
            return null;
        }

        static OldMetadata.METADATA NewToOldMeta(NewMetadata.METADATA input) {
            OldMetadata.METADATA output = new OldMetadata.METADATA();
            output.MDRCD = new OldMetadata.MDRCD();
            output.MDRCD.Version = "5.0";
            output.MDRCD.MdqRequestID = input.MDRCD.MdqRequestID;
            output.MDRCD.WMCollectionID = input.MDRCD.WMCollectionID;
            output.MDRCD.WMCollectionGroupID = input.MDRCD.WMCollectionGroupID;
            output.MDRCD.UniqueFileID = input.MDRCD.UniqueFileID;
            output.MDRCD.AlbumTitle = input.MDRCD.AlbumTitle;
            output.MDRCD.AlbumArtist = input.MDRCD.AlbumArtist;
            output.MDRCD.ReleaseDate = input.MDRCD.ReleaseDate;
            output.MDRCD.Label = input.MDRCD.Label;
            output.MDRCD.Genre = input.MDRCD.Genre;
            output.MDRCD.ProviderStyle = input.MDRCD.ProviderStyle;
            output.MDRCD.PublisherRating = input.MDRCD.PublisherRating;
            output.MDRCD.BuyParams = null;
            output.MDRCD.LargeCoverParams = input.MDRCD.LargeCoverParams;
            output.MDRCD.SmallCoverParams = null;
            output.MDRCD.MoreInfoParams = input.MDRCD.MoreInfoParams;
            output.MDRCD.DataProvider = input.MDRCD.DataProvider;
            output.MDRCD.DataProviderParams = input.MDRCD.DataProviderParams;
            output.MDRCD.DataProviderLogo = input.MDRCD.DataProviderLogo;
            output.MDRCD.NeedIDs = input.MDRCD.NeedIDs;
            output.MDRCD.Track = new List<OldMetadata.Track>();

            if (output.MDRCD.LargeCoverParams != null)
                output.MDRCD.LargeCoverParams = output.MDRCD.LargeCoverParams.Replace("https://musicimage.xboxlive.com/", "");

            foreach (var trackin in input.MDRCD.Track) {
                OldMetadata.Track trackout = new OldMetadata.Track();
                trackout.WMContentID = trackin.WMContentID;
                trackout.TrackRequestID = trackin.TrackRequestID;
                trackout.TrackTitle = trackin.TrackTitle;
                trackout.UniqueFileID = trackin.UniqueFileID;
                trackout.TrackNumber = trackin.TrackNumber;
                trackout.TrackPerformer = trackin.TrackPerformer;
                trackout.TrackComposer = trackin.TrackComposer;
                trackout.TrackConductor = trackin.TrackConductor;
                trackout.Period = trackin.Period;
                trackout.ExplicitLyrics = trackin.ExplicitLyrics;
                output.MDRCD.Track.Add(trackout);
            }
            output.Backoff = new OldMetadata.Backoff();
            output.Backoff.Time = input.Backoff.Time;
            return output;
        }

        static void Main(string[] args) {
            if (args.Length > 0) {
                ip = args[0];
            }

            Console.WriteLine("WindowsMediaRedirect 1.0 - Make Windows Media Player Metadata services work again!");
            Console.WriteLine("\nSet the following hosts entries if you have not done that yet:\n");
            foreach (string host in hosts) {
                Console.WriteLine(ip + "\t" + host);
            }
            listener = new HttpListener();
            listener.Prefixes.Add("http://" + ip + ":80/");

            try {
                listener.Start();
            } catch (Exception) {
                Console.WriteLine("\nCould not bind connection for {0}", "http://" + ip + ":80/");
                Console.WriteLine("\nTry running this application with admin or root privileges");
                return;
            }

            Console.WriteLine("\nListening for connections on {0}", "http://" + ip + ":80/");

            HandleIncomingConnections();

            listener.Close();
        }
    }
}
