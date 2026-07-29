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

        // Variabili globali per memorizzare Artista e Album dell'ultimo CD richiesto
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

                // 1. GESTIONE METADATI CD (Titoli, traccia, artista, album)
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

                        // Salviamo l'Artista e l'Album per usarli nella ricerca su iTunes
                        if (newmeta != null && newmeta.MDRCD != null) {
                            currentArtist = newmeta.MDRCD.AlbumArtist;
                            currentAlbum = newmeta.MDRCD.AlbumTitle;
                            Console.WriteLine("Album identificato: " + currentArtist + " - " + currentAlbum);
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

                // 2. GESTIONE COPERTINE (Preleva l'immagine DIRETTAMENTE da iTunes)
                } else if (req.Url.ToString().StartsWith("http://services.windowsmedia.com/cover/") || 
                           req.Url.ToString().StartsWith("http://images.metaservices.microsoft.com/cover/")) {
                    
                    byte[] data = null;

                    if (!string.IsNullOrEmpty(currentArtist) && !string.IsNullOrEmpty(currentAlbum)) {
                        Console.WriteLine("Ricerca copertina su iTunes per: " + currentArtist + " - " + currentAlbum);
                        string iTunesUrl = GetiTunesCoverUrl(currentArtist, currentAlbum);
                        
                        if (!string.IsNullOrEmpty(iTunesUrl)) {
                            try {
                                WebClient wc = new WebClient();
                                data = wc.DownloadData(iTunesUrl);
                                Console.WriteLine("Copertina scaricata con successo da iTunes!");
                            } catch (Exception ex) {
                                Console.WriteLine("Errore durante il download dell'immagine da iTunes: " + ex.Message);
                            }
                        }
                    } else {
                        Console.WriteLine("Artista o Album non impostati, impossibile cercare su iTunes.");
                    }

                    if (data != null) {
                        resp.ContentType = "image/jpeg";
                        resp.ContentLength64 = data.LongLength;
                        resp.OutputStream.Write(data, 0, data.Length);
                    } else {
                        resp.StatusCode = 404;
                    }

                } else {
                    resp.Redirect(target);
                }

                resp.Close();
            }
        }

        // Funzione per la ricerca su API iTunes (100% C# 2.0)
        static string GetiTunesCoverUrl(string artist, string album) {
            try {
                WebClient wc = new WebClient();
                wc.Encoding = Encoding.UTF8;

                // Tenta di abilitare TLS 1.2 per OS/Framework che lo supportano
                try {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                } catch { }

                string query = Uri.EscapeDataString(artist + " " + album);
                string json = wc.DownloadString("https://itunes.apple.com/search?term=" + query + "&entity=album&limit=1");

                // Estrazione manuale della stringa senza librerie JSON esterne
                int index = json.IndexOf("\"artworkUrl100\":\"");
                if (index != -1) {
                    index += 17;
                    int end = json.IndexOf("\"", index);
                    string url = json.Substring(index, end - index);
                    
                    // Sostituisce la risoluzione 100x100 con 600x600 per avere un'immagine ad alta qualità
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
