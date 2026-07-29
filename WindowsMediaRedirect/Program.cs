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

        // Variabili globali per memorizzare Artista e Album
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

                string urlLower = req.Url.ToString().ToLower();

                // 1. GESTIONE METADATI XML (Intercetta TUTTE le richieste di metadati CD)
                if (urlLower.Contains("getmdrcd") || urlLower.Contains("querytoc")) {
                    string target = "http://musicmatch-ssl.xboxlive.com/cdinfo/GetMDRCD.aspx" + req.Url.Query;
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

                        // Estrazione Artista e Album
                        if (newmeta != null && newmeta.MDRCD != null) {
                            currentArtist = newmeta.MDRCD.AlbumArtist;
                            currentAlbum = newmeta.MDRCD.AlbumTitle;

                            // Se AlbumArtist è vuoto nel DB Microsoft, usa l'artista della prima traccia
                            if (string.IsNullOrEmpty(currentArtist) && newmeta.MDRCD.Track != null && newmeta.MDRCD.Track.Count > 0) {
                                currentArtist = newmeta.MDRCD.Track[0].TrackPerformer;
                            }

                            Console.WriteLine(">>> METADATI CATTURATI CON SUCCESSO:");
                            Console.WriteLine("    Artista: " + currentArtist);
                            Console.WriteLine("    Album:   " + currentAlbum);
                            Console.WriteLine();
                        }

                        StringWriter swriter = new StringWriter();
                        oldSerializer.Serialize(XmlWriter.Create(swriter), NewToOldMeta(newmeta));
                        data = Encoding.UTF8.GetBytes(swriter.ToString());
                    } catch (Exception ex) {
                        Console.WriteLine("Errore recupero metadati: " + ex.ToString());
                        resp.StatusCode = 500;
                        resp.Close();
                        return;
                    }

                    resp.ContentType = "text/xml";
                    resp.ContentEncoding = Encoding.UTF8;
                    resp.ContentLength64 = data.LongLength;
                    resp.OutputStream.Write(data, 0, data.Length);

                // 2. GESTIONE COPERTINE (Scarica da iTunes)
                } else if (urlLower.Contains("/cover/")) {
                    byte[] data = null;

                    if (!string.IsNullOrEmpty(currentArtist) && !string.IsNullOrEmpty(currentAlbum)) {
                        Console.WriteLine(">>> Ricerca copertina su iTunes per: " + currentArtist + " - " + currentAlbum);
                        string iTunesUrl = GetiTunesCoverUrl(currentArtist, currentAlbum);
                        
                        if (!string.IsNullOrEmpty(iTunesUrl)) {
                            try {
                                WebClient wc = new WebClient();
                                data = wc.DownloadData(iTunesUrl);
                                Console.WriteLine(">>> Copertina scaricata con successo da iTunes!");
                            } catch (Exception ex) {
                                Console.WriteLine("Errore download copertina iTunes: " + ex.Message);
                            }
                        }
                    } else {
                        Console.WriteLine("⚠️ ATTENZIONE: Artista o Album non ancora memorizzati.");
                        Console.WriteLine("   Svuota la cache di Media Player ed espelli/reinserisci il CD.");
                    }

                    if (data != null) {
                        resp.ContentType = "image/jpeg";
                        resp.ContentLength64 = data.LongLength;
                        resp.OutputStream.Write(data, 0, data.Length);
                    } else {
                        resp.StatusCode = 404;
                    }

                } else {
                    string target = "http://musicmatch-ssl.xboxlive.com/cdinfo/GetMDRCD.aspx" + req.Url.Query;
                    resp.Redirect(target);
                }

                resp.Close();
            }
        }

        static string GetiTunesCoverUrl(string artist, string album) {
            try {
                WebClient wc = new WebClient();
                wc.Encoding = Encoding.UTF8;

                try {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                } catch { }

                string query = Uri.EscapeDataString(artist + " " + album);
                string json = wc.DownloadString("https://itunes.apple.com/search?term=" + query + "&entity=album&limit=1");

                int index = json.IndexOf("\"artworkUrl100\":\"");
                if (index != -1) {
                    index += 17;
                    int end = json.IndexOf("\"", index);
                    string url = json.Substring(index, end - index);
                    return url.Replace("100x100bb.jpg", "600x600bb.jpg");
                }
            } catch (Exception ex) {
                Console.WriteLine("Errore API iTunes: " + ex.Message);
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
