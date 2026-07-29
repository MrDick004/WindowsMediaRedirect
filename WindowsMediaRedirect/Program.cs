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
                    // A. Gestione richieste metadati (MDR-CD / TOC)
                    if (req.Url.ToString().StartsWith("http://windowsmedia.com/redir/GetMDRCD.asp") || 
                        req.Url.ToString().StartsWith("http://windowsmedia.com/redir/QueryTOC.asp")) {
                        
                        // 1. Recupera metadati da MusicBrainz popolando la struttura OldMetadata
                        OldMetadata.METADATA oldmeta = FetchFromMusicBrainz(req.Url.Query);

                        // 2. Serializza in XML nativo di WMP
                        XmlSerializer oldSerializer = new XmlSerializer(typeof(OldMetadata.METADATA));
                        MemoryStream ms = new MemoryStream();

                        XmlWriterSettings settings = new XmlWriterSettings {
                            Encoding = Encoding.UTF8,
                            Indent = true
                        };

                        using (XmlWriter writer = XmlWriter.Create(ms, settings)) {
                            oldSerializer.Serialize(writer, oldmeta);
                        }

                        byte[] data = ms.ToArray();

                        resp.ContentType = "text/xml";
                        resp.ContentEncoding = Encoding.UTF8;
                        resp.ContentLength64 = data.LongLength;
                        resp.OutputStream.Write(data, 0, data.Length);
                    } 
                    // B. Gestione chiamate per le copertine degli album
                    else if (req.Url.ToString().Contains("/cover/")) {
                        string mbid = ExtractReleaseId(req.Url.ToString());
                        byte[] coverBytes = FetchCoverArt(mbid);

                        if (coverBytes != null && coverBytes.Length > 0) {
                            resp.ContentType = "image/jpeg";
                            resp.ContentLength64 = coverBytes.LongLength;
                            resp.OutputStream.Write(coverBytes, 0, coverBytes.Length);
                        } else {
                            resp.StatusCode = 404;
                        }
                    } 
                    else {
                        resp.StatusCode = 404;
                    }
                } catch (Exception ex) {
                    Console.WriteLine("Errore interno: " + ex.Message);
                    resp.StatusCode = 500;
                } finally {
                    resp.Close();
                }
            }
        }

        // Metodo principale per interrogare l'API di MusicBrainz
        static OldMetadata.METADATA FetchFromMusicBrainz(string queryString) {
            OldMetadata.METADATA meta = new OldMetadata.METADATA();
            meta.MDRCD = new OldMetadata.MDRCD();
            meta.MDRCD.Version = "5.0";
            meta.MDRCD.DataProvider = "MusicBrainz";
            meta.MDRCD.Track = new List<OldMetadata.Track>();

            // Estrazione parametri passati da WMP
            string albumParam = ExtractQueryParam(queryString, "album") ?? "Album Sconosciuto";
            string artistParam = ExtractQueryParam(queryString, "artist") ?? "Artista Sconosciuto";

            try {
                using (WebClient wc = new WebClient()) {
                    // User-Agent IDENTIFICATIVO: Obbligatorio per non farsi bloccare da MusicBrainz
                    wc.Headers.Add("User-Agent", "WindowsMediaRedirectProxy/2.0 ( https://github.com/makuhlmann/WindowsMediaRedirect )");
                    wc.Encoding = Encoding.UTF8;

                    // Query verso MusicBrainz in JSON
                    string searchUrl = string.Format("https://musicbrainz.org/ws/2/release/?query=release:{0}%20AND%20artist:{1}&fmt=json&limit=1", 
                        Uri.EscapeDataString(albumParam), Uri.EscapeDataString(artistParam));

                    string jsonResponse = wc.DownloadString(searchUrl);

                    string mbid = ExtractJsonValue(jsonResponse, "id");
                    string albumTitle = ExtractJsonValue(jsonResponse, "title") ?? albumParam;

                    meta.MDRCD.AlbumTitle = albumTitle;
                    meta.MDRCD.AlbumArtist = artistParam;
                    meta.MDRCD.Genre = "MusicBrainz Match";
                    meta.MDRCD.WMCollectionID = mbid ?? Guid.NewGuid().ToString();

                    // Se viene trovato un MBID valido su MusicBrainz, passiamo l'URL per scaricare la copertina
                    if (!string.IsNullOrEmpty(mbid)) {
                        meta.MDRCD.LargeCoverParams = "http://services.windowsmedia.com/cover/" + mbid;
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine("Impossibile recuperare metadati da MusicBrainz: " + ex.Message);
                meta.MDRCD.AlbumTitle = albumParam;
                meta.MDRCD.AlbumArtist = artistParam;
            }

            meta.Backoff = new OldMetadata.Backoff();
            meta.Backoff.Time = "0";

            return meta;
        }

        // Recupero dell'immagine da Cover Art Archive
        static byte[] FetchCoverArt(string releaseId) {
            if (string.IsNullOrEmpty(releaseId)) return null;

            try {
                using (WebClient wc = new WebClient()) {
                    wc.Headers.Add("User-Agent", "WindowsMediaRedirectProxy/2.0");
                    string coverUrl = string.Format("https://coverartarchive.org/release/{0}/front-250", releaseId);
                    return wc.DownloadData(coverUrl);
                }
            } catch {
                return null;
            }
        }

        // Helper per estrarre argomenti dalla Query String
        static string ExtractQueryParam(string query, string paramName) {
            Match match = Regex.Match(query, paramName + "=([^&]+)", RegexOptions.IgnoreCase);
            return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
        }

        // Helper Regex leggero per deserializzare la risposta JSON senza librerie esterne
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

            Console.WriteLine("WindowsMediaRedirect - Backend MusicBrainz Attivo\n");
            Console.WriteLine("Aggiungi queste voci al file C:\\Windows\\System32\\drivers\\etc\\hosts:\n");

            foreach (string host in hosts) {
                Console.WriteLine(ip + "\t" + host);
            }

            listener = new HttpListener();
            listener.Prefixes.Add("http://" + ip + ":80/");

            try {
                listener.Start();
                Console.WriteLine("\nProxy in ascolto su {0}", "http://" + ip + ":80/");
            } catch (Exception) {
                Console.WriteLine("\nErrore: Esegui l'applicazione come Amministratore per consentire il bind sulla porta 80.");
                return;
            }

            HandleIncomingConnections();
            listener.Close();
        }
    }
}
