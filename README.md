# WindowsMediaRedirect
Questo strumento consente alle versioni più vecchie di Windows Media Player di connettersi ai server dei metadati ospitati da Microsoft.

Versioni supportate:
- Windows Media Player 9 Series
- Windows Media Player 10
- Windows Media Player 11
- Windows Media Player 12 (su Windows 7; Windows 8 e versioni successive non ne hanno bisogno)

## Come usarlo
È sufficiente copiare ed eseguire il file `WindowsMediaRedirector.exe` sul computer di destinazione. Su Windows Vista e versioni successive potrebbe essere necessario avviarlo come Amministratore.

Se desideri configurare questo strumento come server per più dispositivi nella tua rete locale, avvialo semplicemente da riga di comando passando l'indirizzo IP su cui deve rimanere in ascolto come primo parametro. Potrebbe anche essere necessario autorizzare le connessioni nel firewall.

Dopo l'avvio dell'applicazione, verranno mostrate tutte le voci da aggiungere al file `hosts`. Dovrebbe apparire così:

```
127.0.0.1       redir.metaservices.microsoft.com
127.0.0.1       images.metaservices.microsoft.com
127.0.0.1       toc.music.metaservices.microsoft.com
127.0.0.1       windowsmedia.com
127.0.0.1       www.windowsmedia.com
127.0.0.1       services.windowsmedia.com
```

Il file `hosts` si trova solitamente in questi percorsi:

- `C:\WINDOWS\hosts` -- (Windows 98 e Me)
- `C:\WINNT\system32\drivers\etc\hosts` -- (Windows 2000)
- `C:\WINDOWS\system32\drivers\etc\hosts` -- (Windows XP, Vista e 7)

Nota che su Windows Vista e 7 sono necessari i privilegi di amministratore per modificare il file. Il modo più semplice è copiare il file sul desktop, modificarlo e ricopiarlo all'interno della cartella `etc`.

Su Windows 98 e Me il file `hosts` potrebbe non esistere. In tal caso, basta rinominare il file `hosts.sam` in `hosts`. Inoltre, se riscontri problemi, potrebbe essere necessario abilitare la risoluzione DNS nelle impostazioni TCP/IP all'interno delle configurazioni di rete, altrimenti il file `hosts` potrebbe venire ignorato.

## FAQ
### Perché è necessario questo strumento?
Microsoft ha modificato gli URL utilizzati per richiedere le informazioni sui file multimediali in Windows Media Player senza impostare un reindirizzamento corretto per i vecchi domini, che sono tuttora di proprietà di Microsoft.

### Come funziona questo strumento?
Questo strumento crea un piccolo server web che reindirizza alcuni vecchi indirizzi noti verso i nuovi server di metadati. Funziona in combinazione con l'aggiunta di alcune voci nel file `hosts`.

### Perché Windows Media Player 7 o 8 non sono supportati?
Le versioni più vecchie di Windows Media Player richiedono un formato XML completamente diverso, di cui non sono riuscito a trovare esempi o documentazione di riferimento.

### Perché avviene una conversione dei metadati / Perché le immagini degli album non vengono sempre reindirizzate?
Windows Media Player 9 Series è un po' esigente riguardo all'esatto formato XML, quindi ho dovuto convertirlo nello stile che si aspetta. Inoltre, WMP 9 sembra ignorare le risposte HTTP 302 nella maggior parte dei casi quando tenta di scaricare l'immagine dell'album, motivo per cui l'immagine deve essere scaricata e fornita direttamente da questo strumento.

### Perché il codice sembra poco ottimizzato?
Il codice è stato scritto per essere compatibile con C# 2.0, in modo da poter essere compilato utilizzando Visual Studio 2005. Questo garantisce che l'eseguibile (`.exe`) risultante sia compatibile con tutte le vecchie versioni di Windows in grado di eseguire .NET Framework 2.0.

### Perché i pulsanti "Trova informazioni album" o "Visualizza informazioni album" non funzionano?
Questo strumento corregge solo la ricerca automatica dei metadati che avviene la prima volta che un CD Audio viene inserito nel lettore. Quei pulsanti utilizzano un'interfaccia web per consentire la ricerca manuale delle informazioni corrette, tuttavia quel servizio è stato dismesso da molti anni e sarebbe molto difficile da ricreare.

### Sono stati scaricati o modificati metadati errati. Come posso eliminarli tutti?
È sufficiente eliminare tutti i file all'interno di questa cartella:

- `C:\WINDOWS\Application Data\Microsoft\Media Player` -- (Windows 98 e Me)
- `C:\Documents and Settings\Utente\Local Settings\Application Data\Microsoft\Media Player` -- (Windows 2000 e XP)
- `C:\Users\Utente\AppData\Local\Microsoft\Media Player` -- (Windows Vista e 7)
