using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;

namespace WindowsMediaRedirect {
    static class Program {
        public static HttpListener listener;
        public static string ip = "127.0.0.1";
        public static int requestCount = 0;

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

                Console.WriteLine("Richiesta #{0}: {1}", ++requestCount, req.Url.ToString());

                try {
                    // 1. Gestione Metadati CD / Tracce
                    if (req.Url.ToString().StartsWith("http://windowsmedia.com/redir/GetMDRCD.asp") || 
                        req.Url.ToString().StartsWith("http://windowsmedia.com/redir/QueryTOC.asp")) {
                        
                        string queryString = req.Url.Query;
                        OldMetadata.METADATA metadata = FetchMetadataFromMusicBrainz(queryString);

                        XmlSerializer oldSerializer = new XmlSerializer(typeof(OldMetadata.METADATA));
                        MemoryStream ms = new MemoryStream();
                        
                        using (XmlWriter writer = XmlWriter.Create(ms, new XmlWriterSettings { Encoding = Encoding.UTF8 })) {
                            oldSerializer.Serialize(writer, metadata);
                        }

                        byte[] data = ms.ToArray();
                        resp.ContentType = "text/xml";
                        resp.ContentEncoding = Encoding.UTF8;
                        resp.ContentLength64 = data.LongLength;
                        resp.OutputStream.Write(data, 0, data.Length);
                    } 
                    // 2. Gestione Copertine (Cover Art Archive)
                    else if (req.Url.ToString().Contains("/cover/")) {
                        // Estrae l'ID della release MusicBrainz passato nel percorso della copertina
                        string releaseId = ExtractReleaseId(req.Url.ToString());
                        byte[] coverData = FetchCoverArt(releaseId);

                        if (coverData != null && coverData.Length > 0) {
                            resp.ContentType = "image/jpeg";
                            resp.ContentLength64 = coverData.LongLength;
                            resp.OutputStream.Write(coverData, 0, coverData.Length);
                        } else {
                            resp.StatusCode = 404;
                        }
                    } else {
                        resp.StatusCode = 404;
                    }
                } catch (Exception ex) {
                    Console.WriteLine("Errore nell'elaborazione: " + ex.Message);
                    resp.StatusCode = 500;
                } finally {
                    resp.Close();
                }
            }
        }

        // Chiamata all'API MusicBrainz usando WebClient con User-Agent personalizzato
        static OldMetadata.METADATA FetchMetadataFromMusicBrainz(string query) {
            OldMetadata.METADATA meta = new OldMetadata.METADATA();
            meta.MDRCD = new OldMetadata.MDRCD();
            meta.MDRCD.Version = "5.0";
            meta.MDRCD.Track = new List<OldMetadata.Track>();

            // Estrazione di parametri di ricerca basici inviati da WMP
            string albumSearch = ExtractQueryParam(query, "album") ?? "Unknown Album";
            string artistSearch = ExtractQueryParam(query, "artist") ?? "Unknown Artist";

            try {
                using (WebClient wc = new WebClient()) {
                    // MusicBrainz richiede obbligatoriamente un Header User-Agent identificativo
                    wc.Headers.Add("User-Agent", "WMPRedirectProxy/2.0 ( contact@example.com )");
                    wc.Encoding = Encoding.UTF8;

                    // Query di ricerca su MusicBrainz (formato JSON)
                    string searchUrl = string.Format("https://musicbrainz.org/ws/2/release/?query=release:{0}%20AND%20artist:{1}&fmt=json&limit=1", 
                        Uri.EscapeDataString(albumSearch), Uri.EscapeDataString(artistSearch));

                    string jsonResponse = wc.DownloadString(searchUrl);

                    // Estrazione semplificata dei campi via Regex per compatibilità con vecchi .NET
                    string mbid = ExtractJsonValue(jsonResponse, "id");
                    string title = ExtractJsonValue(jsonResponse, "title") ?? albumSearch;

                    meta.MDRCD.AlbumTitle = title;
                    meta.MDRCD.AlbumArtist = artistSearch;
                    meta.MDRCD.Genre = "Rock/Pop"; 
                    meta.MDRCD.WMCollectionID = mbid ?? Guid.NewGuid().ToString();
                    
                    // Se abbiamo trovato un ID album, impostiamo il link per il recupero copertina
                    if (!string.IsNullOrEmpty(mbid)) {
                        meta.MDRCD.LargeCoverParams = "http://services.windowsmedia.com/cover/" + mbid;
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine("MusicBrainz offline o nessun risultato trovato: " + ex.Message);
                meta.MDRCD.AlbumTitle = "Album Sconosciuto";
                meta.MDRCD.AlbumArtist = "Artista Sconosciuto";
            }

            meta.Backoff = new OldMetadata.Backoff();
            meta.Backoff.Time = "0";

            return meta;
        }

        // Recupera l'immagine della copertina da Cover Art Archive (MusicBrainz)
        static byte[] FetchCoverArt(string releaseId) {
            if (string.IsNullOrEmpty(releaseId)) return null;

            try {
                using (WebClient wc = new WebClient()) {
                    wc.Headers.Add("User-Agent", "WMPRedirectProxy/2.0 ( contact@example.com )");
                    string coverUrl = string.Format("https://coverartarchive.org/release/{0}/front-250", releaseId);
                    return wc.DownloadData(coverUrl);
                }
            } catch {
                return null; // Copertina non disponibile
            }
        }

        // Helper per estrarre parametri dalla query string
        static string ExtractQueryParam(string query, string paramName) {
            Match match = Regex.Match(query, paramName + "=([^&]+)", RegexOptions.IgnoreCase);
            return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
        }

        // Helper semplice per estrarre chiavi JSON senza librerie esterne (es. Newtonsoft.Json)
        static string ExtractJsonValue(string json, string key) {
            Match match = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        static string ExtractReleaseId(string url) {
            Match match = Regex.Match(url, @"/cover/([a-f0-9\-]+)");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        static void Main(string[] args) {
            if (args.Length > 0) {
                ip = args[0];
            }

            Console.WriteLine("WindowsMediaRedirect - Powered by MusicBrainz API\n");
            Console.WriteLine("Assicurati che il file HOSTS sia configurato correttamente su " + ip + ":\n");

            foreach (string host in hosts) {
                Console.WriteLine(ip + "\t" + host);
            }

            listener = new HttpListener();
            listener.Prefixes.Add("http://" + ip + ":80/");

            try {
                listener.Start();
                Console.WriteLine("\nProxy in ascolto su {0}", "http://" + ip + ":80/");
            } catch (Exception) {
                Console.WriteLine("\nImpossibile avviare il listener su http://{0}:80/. Esegui l'app come Amministratore.", ip);
                return;
            }

            HandleIncomingConnections();
            listener.Close();
        }
    }
}
