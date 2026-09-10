# Modifiche del fork — che cosa è cambiato, e perché

Questo file racconta a chi non ha seguito giorno per giorno **che cosa abbiamo cambiato nel fork
e per quale ragione**. Racconta idee e decisioni, non codice: chi vuole il dettaglio tecnico ha i
messaggi di commit e le spec in `docs/superpowers/specs/`.

Base: `ours/integration` sopra `master`. Al 2026-09-09 sono **80 commit** raggruppati in **23 rami
`stage/*`**, ognuno tenuto integrabile su `master` per conto proprio. Tutto quello che c'è qui è
girato in produzione sul server dal 2026-09-09, con la suite verde (2756 test, 9 saltati, 0 rossi).

---

## Come si scrive questo file

Chi tocca il fork da qui in avanti aggiunge la sua voce qui dentro, con queste regole.

**Una voce per cambiamento concettuale, non per commit.** Se tre commit servono la stessa idea,
sono una voce sola. Se un commit contiene due idee, sono due voci.

**Il lettore non legge il codice.** Niente nomi di classi, di metodi o di file sorgente, niente
diff, niente numeri di riga. I nomi che si vedono da fuori — un marcatore come `ATTACH:`, un
comando come `/resume` — si possono nominare, perché sono l'interfaccia, non l'implementazione.

**Prima il principio, il difetto come contorno.** La domanda a cui la voce risponde è «qual è la
regola che adesso vale», non «quale bug abbiamo chiuso». L'incidente serve come prova che la regola
serviva, e va messo dopo, in una riga o due. Una voce che è solo il racconto di un bug è scritta
male.

**Le misure solo se misurate.** «26 uccisioni su 29» va bene perché è stato contato; «molto più
veloce» no. Se un numero è una stima, si dice che è una stima.

**Anche il prezzo pagato.** Se un cambiamento ha peggiorato qualcosa d'altro, si scrive nella voce.
Un lettore che scopre il prezzo da solo, dopo, smette di fidarsi del resto del file.

**La forma di una voce:**

> ### Titolo — la regola, in parole piane
>
> **Com'era.** Il comportamento di prima, in una o due frasi.
> **Cos'è adesso.** Il comportamento nuovo.
> **Perché.** Il principio, e poi l'incidente o la misura che lo hanno reso evidente.
> **Cosa cambia per chi lo usa.** Solo se cambia davvero qualcosa sotto gli occhi.
> **Dove.** I rami `stage/*`, per chi poi vuole andare a vedere.

**Dove metterla.** In fondo alla sezione tematica giusta. Se nessuna sezione va bene, se ne apre
una nuova; l'ordine dentro una sezione non ha significato. La sezione 6 è l'unica che parla di
lavoro non ancora fatto: chi ci scrive dentro mette la data, e chi finisce una di quelle cose la
sposta su, in una voce vera.

**Se un cambiamento successivo ne annulla uno precedente**, non si cancella la voce vecchia: si
riscrive dicendo che cosa è stato ritirato e perché. La storia di una decisione cambiata vale più
della decisione.

**La lingua di questo file è l'italiano**, perché è scritto per una persona. Tutto il resto —
codice, file, commit, voci di canale fra agenti — resta in inglese.

---

## 1. Come il sistema parla con il proprietario

### Una domanda è completa, o non è una domanda

**Com'era.** Un agente chiedeva come gli veniva. Se mancava la riga della domanda, l'app la
ricavava dall'ultima frase del testo e, se non ci riusciva, ci metteva un «decidi tu» prefabbricato.

**Cos'è adesso.** Una domanda ha una forma obbligatoria: la domanda, almeno due opzioni, una
raccomandazione, il livello di rischio e la riga di piano a cui si riferisce. Se è incompleta il
testo arriva lo stesso al proprietario — un errore di forma non deve mai costargli un messaggio —
ma non gli crescono i bottoni sotto, e l'agente riceve **un solo** messaggio che elenca tutto
quello che manca, non uno per volta.

**Perché.** Una domanda con le opzioni e una raccomandazione è una decisione presa in trenta
secondi; la stessa domanda senza è una decisione rimandata di una settimana. Misurato su un
argomento il 2026-09-07: quaranta minuti di domande fatte una alla volta, parecchie senza opzioni,
nessuna con una raccomandazione. E i due ripieghi (domanda dedotta, «decidi tu») erano proprio il
motivo per cui la forma non veniva mai corretta: coprivano il difetto.

**Cosa cambia per chi lo usa.** Raccomandazione e riga di piano stanno accanto alla domanda, dove
si possono leggere e decidere dalla schermata di blocco del telefono.

**Dove.** `stage/3-owner-questions-and-attachments`.

### «Parliamone» — un bottone che non spende niente

**Com'era.** Per capire una domanda bisognava rispondere, e rispondere significava chiuderla.

**Cos'è adesso.** Ogni domanda offre anche «💬 Parliamone». La domanda resta viva e segnata come in
discussione: finché si parla, quello che si scrive non chiude niente, e solo un tocco su un'opzione
decide.

**Perché.** Il proprietario rispondeva alle domande con domande — «di che metodi parli?», «quale
parte?». Se ogni parola scritta chiude qualcosa, chiedere un chiarimento diventa rischioso, e allora
si smette di chiederlo.

**Dove.** `stage/3-owner-questions-and-attachments`.

### Una risposta appartiene alla sua domanda

**Com'era.** Qualsiasi messaggio del proprietario chiudeva tutto ciò che era aperto, e le sue parole
venivano archiviate come risposta sotto ogni domanda che chiudeva. Il proprietario ha visto la sua
stessa domanda — «a che punto siamo?» — registrata come risposta a una domanda su un merge, due
volte; e una riga breve chiudere quattro domande insieme, tre delle quali nessuno aveva risposto.

**Cos'è adesso.** L'abbinamento si fa solo quando è sicuro. Se non c'è niente di aperto, se il
messaggio è a sua volta una domanda, o se ce ne sono due o più aperte, non si abbina niente: le
domande restano aperte, con i bottoni vivi e i solleciti attivi.

**Perché.** Il sistema ci credeva già, per i tocchi: un bottone porta con sé un codice non
indovinabile, apposta perché un bottone vecchio non possa rispondere a una domanda per cui non era
stato offerto. Una riga scritta a mano quel codice non ce l'ha, quindi dove l'attribuzione non si
può dedurre l'unica cosa onesta è non attribuire. La direzione dell'errore è scelta: nel caso
peggiore il proprietario tocca un bottone, un gesto; l'errore opposto è una decisione messa a
verbale che nessuno ha preso.

**Dove.** `stage/2a-an-answer-belongs-to-its-question`.

### Il lucchetto ad alto rischio legge la domanda, non il racconto

**Com'era.** Le domande pericolose (mettere in produzione, cancellare, forzare) si sbloccano con un
codice a quattro cifre. Il riconoscimento cercava le parole dentro tutto il testo, pezzo di parola
compreso: in un pomeriggio ha messo il lucchetto a quattro domande di puro prodotto perché nel
racconto attorno comparivano frasi come «il motore in produzione va in crash su queste chiavi».
Non si stava mandando in produzione niente.

**Cos'è adesso.** Guarda la domanda e le etichette delle opzioni — cioè quello che il proprietario
sta effettivamente decidendo — e riconosce parole intere: «lo pubblico adesso?» scatta, «l'avevo già
pubblicato» no.

**Perché.** Un falso positivo non è gratis: è un codice davanti a una decisione che non ne aveva
bisogno, e un lucchetto che scatta su quello che un agente ha nominato per caso è un lucchetto che
si impara a digitare senza leggere. Allora, il giorno che serve, non protegge più niente.

**Dove.** `stage/3-owner-questions-and-attachments`.

### Un file è una consegna, e non può sparire in silenzio

**Com'era.** Gli agenti potevano allegare solo immagini. Il 2026-09-08 quattro mockup HTML che il
proprietario aveva chiesto sono partiti come immagini, Telegram li ha rifiutati uno per uno, l'app
ha scritto un avviso in un registro che non legge nessuno e non l'ha detto a nessuno: né al
proprietario né all'agente. Il supervisore gli ha poi detto in buona fede di averli mandati, e ha
sostenuto quella premessa falsa per tre messaggi.

**Cos'è adesso.** Esiste `ATTACH:` per mandare un documento, con una regola su da dove può uscire
un file — la cartella del progetto, quella della supervisione, quella dei mockup — e i limiti di
dimensione di Telegram. Immagini e documenti passano da un unico cancello che **scrive sempre il
rifiuto nel canale dell'agente, con dentro il rimedio**: quale marcatore usare, quale limite è stato
superato, quali cartelle sono ammesse. E un file arriva al proprietario anche se non è una risposta
a una domanda, perché una consegna è fatta per essere guardata.

**Perché.** Un fallimento silenzioso è la forma peggiore che un guasto può prendere qui, perché la
sessione continua a ragionare partendo da lì. Un errore detto è un errore che costa un minuto; un
errore taciuto diventa una discussione fra due persone che hanno in testa due mondi diversi.

**Dove.** `stage/3-owner-questions-and-attachments`, `stage/3a-a-file-never-vanishes-in-silence`.

### Un'entry lunga si piega sul telefono

**Com'era.** Tre messaggi da quattromila caratteri di fila, uno dietro l'altro.

**Cos'è adesso.** Sopra una certa lunghezza arriva il primo paragrafo in chiaro e il resto dentro una
citazione richiudibile, che si apre con un tocco. Se anche così è enorme, l'entry arriva **in più** come
file allegato. Non si perde niente e non si taglia niente.

**Perché.** Il canale è la finestra del proprietario su tutto: se leggerlo dal telefono costa fatica,
smette di guardarlo, e allora il sistema è cieco dalla parte che conta.

**Dove.** `stage/1j-long-entries-fold-on-the-phone`.

### Meno notifiche che non chiedono niente

**Com'era.** Sette avvisi sul ciclo di vita dei membri in due ore, fra cui la chiusura di ogni
membro.

**Cos'è adesso.** La chiusura di un membro resta fra gli agenti. L'apertura invece continua ad
arrivare, e apposta: una sessione che parte è una sessione che comincia a spendere, e quelli sono
soldi del proprietario.

**Perché.** Un avviso su cui non c'è niente da fare e niente da disfare non va al telefono. È la
stessa regola per cui una lamentela sulla forma del piano va al supervisore e non al proprietario:
è il supervisore che può sistemarla.

**Dove.** commit diretto su `ours/integration` (`5c29cd6`).

### Il traduttore non c'è più

**Com'era.** L'app traduceva in italiano i messaggi diretti al proprietario prima di mandarli su
Telegram, con un interruttore per accenderlo e spegnerlo.

**Cos'è adesso.** L'app non traduce niente. Se ne occupano gli agenti, da soli: al proprietario
rispondono nella lingua in cui il proprietario ha scritto.

**Perché.** Un modello le lingue le sa già — fargli scrivere in inglese e poi tradurre è la stessa
frase scritta due volte, con un processo in più e un punto in più dove rompersi. La regola che conta
non è «in che lingua scrive l'agente», è **dove finisce il testo**: quello che resta sul disco o va a
un altro agente — file, codice, commit, il piano, le voci dei canali — è in inglese, perché è
materiale di lavoro condiviso; quello che è rivolto a una persona è nella lingua di quella persona.
Detta così, la regola sta in una riga nel comando di ruolo e non serve nessun meccanismo.
*Di contorno: tenuto acceso faceva anche danno — prendeva prosa già italiana, la passava a una
traduzione inglese→italiano, e il testo tornava indietro identico; a quel punto l'app lo marchiava
come «non tradotto» e metteva una bandierina d'avviso su un messaggio corretto. Nei registri recenti
contava 183 fallimenti, ognuno un processo in più, e ogni scambio col proprietario ne pagava due.*

**Cosa cambia per chi lo usa.** Niente interruttore, niente comando, un processo in meno per
messaggio. Su Telegram si continua a parlare in italiano.

**Dove.** `stage/7c-no-translation-layer`, `stage/7h-remove-translator`.

---

## 2. La vita di un turno

### Un membro occupato non è un membro morto

**Com'era.** Un controllo dichiarava «orfano» un membro che non consumava il suo canale da un po'.
In tre ore, su una sola orchestrazione, ha sparato diciannove volte, e tutte e diciannove erano
false: un membro ha finito il suo turno diciotto secondi dopo essere stato dichiarato inattivo, un
altro ha consegnato un rapporto coerente undici secondi dopo.

**Cos'è adesso.** «Non lo so» è una risposta a sé, e non fa scattare niente. Un turno in corso è
vita. E l'uccisione del processo non è stata ristretta: è stata **tolta**. Quando il sospetto è
fondato, adesso si chiede al supervisore di andare a vedere.

**Perché.** Il controllo misurava quanto un membro fosse occupato o bloccato, non se fosse vivo: chi
sta dentro un turno lungo non può leggere il suo canale, quindi veniva condannato proprio perché
stava lavorando. La conferma sta nel confronto: un'orchestrazione sorella con cinque membri,
cinquantotto turni e due ore di lavoro non è stata toccata nemmeno una volta — più intensa, ma senza
un solo turno oltre i cinque minuti, contro undici e un massimo di ventuno qui. E la funzione di
uccisione era stata introdotta una volta e ristretta cinque, ogni volta reagendo a un falso
positivo, senza che da nessuna parte risultasse un solo caso in cui avesse salvato qualcuno.

**Dove.** `stage/2b-a-busy-member-is-not-a-dead-one`.

### «Sta lavorando?» — tre risposte, non due

**Com'era.** Tutte le risposte a questa domanda venivano da un file che scrive la barra di stato di
Claude Code, e che una sessione senza interfaccia non scrive mai. Quindi su ogni macchina governata
dal bridge il file non c'era, e «non lo so» veniva letto come «no». Il proprietario si è sentito
dire «inattivo, in attesa» per due ore su cinque membri che stavano tutti lavorando; la parola
«sta lavorando» non è comparsa una volta.

**Cos'è adesso.** L'app risponde da quello che ha scritto lei: è lei che fa partire i turni, tiene
l'elenco di quelli in volo e registra quando finiscono. E la risposta ha tre valori: sta lavorando,
è fermo, **non lo so**.

**Perché.** «Non lo so» non è un modo di dire «no». Confondere le due cose è ciò che ha trasformato
un file mancante in una decisione distruttiva. Vale in generale: se una fonte non c'è, il sistema
deve dirlo, non dedurne il contrario.

**Dove.** parte di `stage/2b`.

### L'orologio del silenzio parte quando parte il prompt

**Com'era.** Un turno veniva ucciso «per silenzio» se non arrivavano dati per un tot di tempo, ma il
cronometro non veniva azzerato quando si mandava una nuova richiesta: contava dal traffico
precedente. Quindi qualunque turno mandato dopo una pausa più lunga del limite veniva ucciso subito,
e ritentato un minuto dopo su un processo freddo.

**Cos'è adesso.** Il cronometro parte quando parte la richiesta.

**Perché e quanto.** Ventisei uccisioni per silenzio su ventinove erano di questa forma — il 6% dei
risvegli del supervisore, che passavano da ~25 secondi a 111 secondi mediani per arrivare a
destinazione. Due orchestrazioni hanno risposto al proprietario alle 4:47 del mattino solo dopo una
di queste uccisioni.

**Dove.** `stage/7a-stream-heartbeat`.

### Un rifiuto per limite d'uso compra un appuntamento

**Com'era.** Quando l'account tocca il limite d'uso, l'interfaccia dice a che ora si riapre la
finestra. L'app buttava via quella frase: tre tentativi a un minuto l'uno dall'altro, poi silenzio
finché non arrivava traffico nuovo. Diciotto stalli così, da 167 a 509 minuti l'uno, due
orchestrazioni ferme per un'intera notte con il proprietario che aspettava.

**Cos'è adesso.** L'ora viene letta e diventa un appuntamento: nessun tentativo sprecato,
l'appuntamento sopravvive a un riavvio, il proprietario viene avvisato nel canale di quando si
riparte, e `/resume` lo annulla.

**Perché conta anche il seguito.** La prima versione di questa correzione era peggio del difetto, e
l'ha dimostrato una revisione avversaria: un'ora letta un secondo dopo essere passata veniva
spostata al giorno dopo, e ogni ulteriore rifiuto aggiungeva un altro giorno, senza che nessun
traffico potesse svegliare la sessione. E il riconoscimento era la parola «limite»: un turno
**riuscito**, il cui unico problema era un canale su cui non riusciva a scrivere, veniva parcheggiato
quattordici ore senza spendere un tentativo — quindi senza mai fermarsi né dare l'allarme. Adesso
l'appuntamento è limitato a sei ore (la finestra di sessione è cinque, quindi ogni riapertura che una
sessione può nominare ci sta dentro; un limite settimanale nomina un giorno più in là, ed è una
lettura che non si può verificare), e per essere un rifiuto ci vuole il codice del rifiuto, non una
parola.

**Il principio.** Una lettura che non si capisce vale **niente**, mai una supposizione: il sistema
torna a comportarsi come prima e scrive una riga che dice esattamente che cosa non ha saputo leggere.

**Dove.** `stage/7b-limit-reset-retry`, `stage/7i-limit-reset-fixes`.

### Un turno ucciso alla scadenza dice dove è arrivato

**Com'era.** Dopo trenta minuti il turno veniva ucciso e, un minuto dopo, lo stesso lavoro
ricominciava da zero. Undici casi, tutti implementatori, ognuno trenta minuti buttati e poi rifatti
in un tempo fra un minuto e un quarto d'ora. Non erano bloccati — un processo muto viene già ucciso
prima — stavano lavorando.

**Cos'è adesso.** La scadenza non vuol più dire «butta via». Vuol dire un ultimo turno di chiusura,
al massimo cinque minuti, con una sola istruzione: non continuare, scrivi che cosa è fatto, che cosa
è a metà e che cosa viene dopo, poi fermati. Quel rapporto entra nel canale come qualsiasi altra
voce, quindi sveglia il supervisore, e il lavoro non viene riproposto da capo.

**Perché.** Trenta minuti di ragionamento sono la cosa più costosa che questo sistema produce.
Buttarli via e rifarli non è solo lento: la seconda volta si prendono decisioni diverse, e nessuno
sa quali. Vale ancora di più da quando le sessioni ripartono pulite, perché prima almeno restava la
trascrizione.

**Il prezzo trovato dopo.** Anche qui una revisione avversaria ha trovato il difetto grosso: il
turno di chiusura marcava come «consegnati» tutti i messaggi in attesa, mentre il suo rapporto è una
voce sola indirizzata dove gli pare. Un supervisore ucciso mentre rispondeva al proprietario poteva
scrivere il rapporto altrove, e la domanda del proprietario risultava consegnata senza che restasse
nessuno a occuparsene. Adesso si consuma solo ciò a cui si è davvero risposto.

**Un limite che non limita, detto perché non ci si conti.** Il turno di chiusura ha un tetto di
spesa dichiarato, ma quel tetto è controllato **dopo** il passo, non prima: misurato, con un tetto
di 0,0001 dollari ne sono stati spesi 0,11. Quello che davvero delimita il turno di chiusura è il
suo tempo massimo, non il tetto.

**Dove.** `stage/7f-timeout-closing-turn`, `stage/7j-closing-turn-fixes`.

### Uno stop svuota prima di uccidere

**Com'era.** Fermare il servizio chiudeva la porta e ammazzava i turni in corso nello stesso istante.
In 44 ore di lavoro ci sono stati 21 riavvii (le installazioni), e diciassette turni in volo — 112
milioni di token, il 6,4% di tutto il consumo — sono morti entro quattro minuti da una riga «mi sto
fermando», per poi essere rifatti da zero.

**Cos'è adesso.** Due segnali separati: uno chiude la porta ai turni nuovi, l'altro arriva solo dopo
che quelli in corso hanno avuto il tempo di finire da soli.

**E il seguito, che vale come lezione.** Non bastava. Il servizio annunciava «sto svuotando, fino a
31 minuti» e il sistema operativo lo dichiarava spento **esattamente trenta secondi dopo**, tre
riavvii di fila: era un tempo di attesa predefinito dell'ospite di processo, che nessuno aveva mai
impostato. Cinque sessioni fresche morte a metà turno, quindici milioni di token. Adesso una regola
sola dimensiona i tre tempi in scala, dal più interno al più esterno.

**Il principio.** Un periodo di grazia deve essere rispettato da ogni strato sotto di lui, altrimenti
vince in silenzio il valore predefinito di quello più esterno — e l'annuncio che il sistema fa di sé
diventa una bugia.

**Dove.** `stage/4a-fresh-turns-and-drain`, `stage/4e-host-shutdown-timeout`.

---

## 3. La memoria fra un turno e l'altro

### Una sessione fresca riceve la memoria in un pacchetto

**Com'era.** Una sessione che riparte pulita si avviava con il solo comando di ruolo: i messaggi che
il bridge aveva appena deciso di consegnarle non le arrivavano, e allora se li andava a cercare da
sola, aprendo canali e piani. Misurato sul supervisore generale: 9,2 chiamate al modello per turno,
di cui 7,8 solo per leggere file. E l'83% di tutto il contesto consumato sul server (1456 milioni di
token su 1756) era roba trascinata dai turni precedenti.

**Cos'è adesso.** L'app prepara un pacchetto e glielo mette in mano: i messaggi in attesa, il brief,
l'ultimo rapporto che quel membro ha scritto, le righe di piano che lo riguardano, lo stato del
codice. Il ruolo lo legge per primo e tratta il canale come una consultazione, non come il punto di
partenza.

**Perché.** Ogni cosa che una sessione deve andare a cercare è pagata due volte: una per cercarla e
una per rileggerla al turno dopo. Chi sa già che cosa serve — il bridge, che ha appena deciso lui
quali messaggi sono in attesa — deve consegnarlo, non lasciarlo indovinare.

**Il dettaglio che regge tutto.** Una sezione che non si riesce a leggere viene **nominata** come
non riempita, invece di comparire vuota: un piano assente non deve leggersi come un piano senza
righe. E un pacchetto che non si riesce a scrivere non ferma mai il turno.

**Dove.** `stage/4a-fresh-turns-and-drain`, `stage/4b-state-pack`.

### Il brief si ritrova anche dopo che il canale è stato compattato

**Com'era.** A una sessione fresca si davano «le ultime tre voci», che dopo un giro di revisione non
contengono più l'incarico.

**Cos'è adesso.** L'incarico si cerca con una regola deterministica, e la ricerca attraversa sia il
canale vivo sia l'archivio.

**Perché.** I canali vengono compattati: la parte vecchia si sposta in un archivio. Qualsiasi cosa
conti «quante voci ci sono» o «le ultime N» guardando solo il file vivo prima o poi sbaglia, e
sbaglia in silenzio.

**Dove.** `stage/4b-state-pack`.

### Strumenti per misurare il consumo

**Cos'è.** Script di sola lettura che dicono quante chiamate costa un turno, quanto contesto porta
la prima chiamata, e quante di quelle chiamate sono lavoro vero contro quante sono orientamento.
Leggono solo metadati e non scrivono niente fuori dai file temporanei.

**Perché.** Le decisioni di questo capitolo sono tutte compromessi fra costo e comodità. Senza una
misura presa prima e dopo, sullo stesso punto, sono opinioni.

**Dove.** `stage/4b-state-pack`.

### Implementatori e revisori ripartono puliti a ogni turno

**Com'era.** Ogni sessione continuava la conversazione precedente, quindi si portava dietro tutto
quello che aveva già letto e detto, turno dopo turno.

**Cos'è adesso.** Implementatori e revisori ricominciano da zero a ogni turno, e ricevono in mano il
pacchetto di stato descritto qui sopra. È una modifica di configurazione, non di codice: è
l'interruttore che accende tutto il lavoro di questa sezione.

**Perché e quanto.** L'83% del contesto consumato sul server era roba trascinata dai turni
precedenti — pagata di nuovo a ogni chiamata, senza che nessuno l'avesse chiesta. Dopo il
cambiamento, misurato: i token per chiamata di un membro passano da 345 mila a circa 100 mila e
restano stabili; su un compito breve, il costo per consegna scende di circa 4,7 volte.

**Dove è finito il costo che resta.** Non è sparito: si è spostato **dentro** i turni lunghi. Il 41%
dei token dei membri in una serata sta in turni arrivati fino alla scadenza dei trenta minuti. È il
prossimo pezzo di lavoro (vedi la sezione 6).

**Dove.** configurazione del server, e `stage/4b-state-pack` per il pacchetto che la rende possibile.

---

## 4. Velocità e costo dell'app

### Una scrittura su un canale sveglia il ciclo

**Com'era.** Un messaggio del proprietario ci metteva 11-12 secondi a raggiungere il supervisore e
36-42 a ottenere risposta. Di quel tempo l'app se ne teneva una decina: sei secondi per aggregare
messaggi vicini, fino a due di attesa del giro di controllo, tre di raggruppamento.

**Cos'è adesso.** Un osservatore del file system accorcia l'attesa appena qualcuno scrive; il giro
di controllo periodico resta come rete di sicurezza, e una macchina che non manda quelle notifiche
si comporta esattamente come prima, con una riga nel registro che lo dice. La finestra di
aggregazione per il proprietario si è dimezzata, mentre per i membri resta intera, perché ogni
risveglio del supervisore in più costa circa un milione di token in ingresso.

**Il prezzo, scritto perché è reale.** Accelerando, il margine che impedisce di specchiare una voce
scritta a metà era stato ridotto di dodici volte, e uno scrittore che si fermava quattro decimi di
secondo si vedeva la voce spedita troncata e il resto distrutto. Adesso quel margine è di nuovo una
durata, non un conteggio di giri, e vale quello che valeva prima. La conseguenza è che **l'ultima
voce di un canale ci mette di nuovo quattro secondi** ad arrivare sul telefono: è il prezzo di non
troncarla. Il guadagno sul percorso del proprietario resta, perché quel percorso non passa di lì.

**E un dettaglio che vale un milione di token.** Un messaggio già finito (che termina con un punto)
saltava del tutto la finestra di attesa: due frasi scritte a due secondi di distanza compravano due
turni di supervisore. Adesso anche un messaggio finito aspetta un paio di secondi.

**Dove.** `stage/7e-owner-path-latency`, `stage/7k-waker-fixes`.

### Il giro di controllo legge ciò che è cambiato

**Com'era.** Ogni due secondi il ciclo rileggeva tutto: l'elenco delle sessioni tredici volte, i
canali per intero anche solo per scoprire che non c'era niente da compattare, e riscriveva
comunque il file dei segnaposti.

**Cos'è adesso.** L'elenco si carica una volta per giro; il controllo di compattazione conta le
intestazioni invece di leggere tutto; i canali si rileggono solo se sono cambiati davvero
(dimensione e data, mai «sono passati tot secondi»); i segnaposti si scrivono solo se si sono
mossi.

**Quanto.** Da 195 aperture di file e una scrittura per giro a 44 aperture e zero scritture: -77%.
Niente di ciò che il bridge decide è cambiato.

**Dove.** `stage/7g-tick-hygiene`.

### Il modello di ogni membro è scelto sul compito

**Com'era.** Un modello fisso per ruolo.

**Cos'è adesso.** Chi apre una sessione ne indica il modello, e la scelta resta scritta nella scheda
del membro, così una sessione che viene fatta ripartire torna sul modello per cui il compito era
stato dimensionato. Ci sono tre livelli di precedenza: quello che ha imposto il proprietario per
quell'orchestrazione, quello del membro, il valore predefinito. Il modello più costoso lo può
scegliere solo il proprietario.

**Perché.** Una correzione di una riga e una riprogettazione non sono lo stesso mestiere, e non
devono costare uguale. Il criterio è scritto nel manuale del ruolo — il modello economico per il
lavoro meccanico e delimitato, quello grosso per il progetto, il giudizio, i percorsi che toccano
soldi e le revisioni profonde; nel dubbio si sale di un livello — e la scelta viene detta al
proprietario **insieme alla ragione**, così vede per che cosa sta pagando.

**Dove.** `stage/1j-implementer-model`.

### La memoria di un turno gliela consegna l'app, non se la va a cercare

**Com'era.** Ogni sessione ricordava tutta la propria conversazione e la rimandava al modello a ogni
mossa: il contesto cresceva per tutto il giorno e non si azzerava mai. Quando le sessioni sono
diventate fresche — una per turno — il trascinamento è sparito, ma la sessione nuova si metteva a
cercare da sola quello che le serviva, rileggendo il canale da cima a fondo.

**Cos'è adesso.** Prima di svegliare una sessione, il bridge le scrive un **pacchetto**: le voci che
l'hanno svegliata, l'incarico che sta lavorando (cercato anche nell'archivio, non solo nella parte
recente del canale), il suo ultimo rapporto, lo stato del codice, e le righe del piano che la
riguardano. La sessione lo legge come prima cosa e usa il canale solo per un fatto che al pacchetto
manca. Al supervisore, quando toccherà a lui, il pacchetto porta anche il piano intero e la coda dei
messaggi del proprietario.

**Perché.** Ciò che una sessione va a cercare l'app lo ha già in mano: farglielo cercare è pagare due
volte, una in lettura e una in contesto. **Misurato** il 2026-09-09: le sessioni fresche aprivano il
proprio canale 7,8 volte per turno prima, circa una volta dopo; il contesto della prima chiamata è
passato da 249.000 token a 39.000, e il costo per chiamata dei membri da 345.000 a circa 100.000 —
stabile su tre finestre di misura diverse.

**Il prezzo pagato, detto chiaro.** Il guadagno per *turno* è più piccolo di quello per chiamata
(1,5 volte contro 3,4), perché i turni si sono allungati: un membro fresco con un incarico grosso
lavora fino alla scadenza dei trenta minuti. Il 41% dei token di quella sera stava in turni arrivati
alla scadenza. È il problema che viene affrontato adesso, non uno risolto.

**Una cosa imparata sulla forma, non sul contenuto.** Il pacchetto viaggia in un **file**, non
attaccato al messaggio: misurato che il testo passato per quella strada viene appiccicato al comando
di ruolo e finisce dentro il suo argomento, e ci sono ruoli che quell'argomento lo usano per
costruire percorsi. La strada comoda era anche quella che rompeva.

**Dove.** `stage/4b-state-pack`.

### Una promessa di spegnimento che il processo stesso tagliava

**Com'era.** Quando il servizio veniva fermato, l'app annunciava di aspettare i turni in corso —
fino a trentuno minuti — e trenta secondi dopo il sistema la dichiarava spenta. I turni a metà
morivano e ricominciavano da zero.

**Cos'è adesso.** L'attesa dichiarata è quella vera: i turni in volo arrivano alla fine, e solo dopo
le sessioni vengono chiuse. La prima fermata con la correzione attiva ha aspettato ventuno minuti e
ha scritto in chiaro «drenaggio completo, nulla da rifare».

**Perché era sfuggito.** Il tempo di spegnimento non era stato impostato da nessuno, quindi valeva il
predefinito di trenta secondi: l'ordine di grandezza sbagliato di sessanta volte. E la spiegazione
che tutti avevano in mano era un'altra — si dava la colpa alla configurazione del server, che invece
concedeva trentacinque minuti; il taglio era del processo. **Misurato** il 2026-09-09: tre riavvii in
sei minuti hanno ucciso cinque sessioni fresche, quindici milioni di token, e lo stesso turno è
ripartito da zero quattro volte.

**Il principio.** Un'attesa promessa in un messaggio e non impostata nel codice è una promessa che
non esiste. Le tre soglie in gioco — quanto aspetta chi lavora, quanto aspetta l'app, quanto aspetta
il sistema operativo — ora si calcolano da un unico posto, l'una sopra l'altra, e se qualcuno
configura un turno più lungo di quanto lo spegnimento possa reggere l'app lo dice all'avvio.

**Cosa cambia per chi lo usa.** Fermare il servizio adesso può prendere minuti invece di secondi. È
il prezzo giusto: prima era istantaneo perché buttava via il lavoro.

**Dove.** `stage/4e-host-shutdown-timeout`.

### Un turno lungo viene avvisato una volta, e il conto lo dice per intero

**Com'era.** Un turno lungo veniva tagliato dall'orologio: alla scadenza dei trenta minuti la sessione
era uccisa e le si chiedeva un rapporto. Il punto in cui si fermava lo scegliava la scure.

**Cos'è adesso.** Passate circa trentacinque chiamate, alla sessione arriva **un solo avviso**: sei a
questo punto, se sei arrivato a qualcosa di stabile salva e racconta, se sei a metà di una modifica
finiscila prima. Non è un divieto: nulla viene bloccato, e continuare è sempre permesso. La scadenza
resta la rete dura di prima.

**Perché.** Con le sessioni fresche il trascinamento tra turni è finito, e la spesa si è concentrata
**dentro** i turni lunghi: **misurato** il 2026-09-09, il 41% dei token dei membri stava in turni
arrivati fino alla scadenza, e il contesto dentro uno di quei turni sale attorno ai 200.000, quindi
ogni chiamata in più costa più della precedente. Un turno che finisce in un punto scelto costa meno di
uno tagliato a caso.

**Quello che questa voce racconta più della funzione.** La prima versione **non funzionava affatto** e
i suoi ventuno test erano verdi: due informazioni lette dal messaggio della CLI si fondevano in una
quando la seconda mancava — e mancava sempre, sulle sessioni che questo sistema avvia. I test
passavano perché il messaggio finto che usavano conteneva un campo che quello vero non ha. Da lì due
regole: **il messaggio finto di un test parte dalla forma vera**, e un test si giudica da una domanda
sola — passerebbe anche se la funzione fosse spenta?

**Cosa cambia per chi lo usa.** Nulla di visibile, se non che i turni lunghissimi dovrebbero
diradarsi. L'avviso dice anche una cosa che prima non diceva: il numero di chiamate è **del turno**,
non solo di chi lo riceve — un agente che ha delegato letture in parallelo ne ha spese molte senza
vederle nella propria cronologia.

**Dove.** `stage/4g-soft-boundary`.

### Il supervisore viene svegliato per decidere, non per prendere nota

**Com'era.** Ogni riga scritta da un membro svegliava il supervisore, e ogni suo risveglio gli fa
rileggere tutta la propria memoria. **Misurato** dal 6 al 9 settembre: circa 400 risvegli, **247
causati dai membri**, con un contesto medio di 398.000 token per chiamata — l'ordine di un milione di
token per un risveglio, e il 15% dei suoi turni era lungo quanto un cenno del capo.

**Cos'è adesso.** I messaggi del proprietario lo svegliano subito, come prima, su qualunque canale. Lo
sveglia subito anche un membro che dichiara di essere bloccato o che fa una domanda, e il primo
messaggio di un membro appena creato. I rapporti ordinari invece **si raccolgono**, per non più di
cinque minuti, e viaggiano insieme: un equipaggio che consegna a raffica compra un turno, non tre. Gli
avvisi meccanici dell'app non lo svegliano affatto — e non lo facevano già, cosa che si è scoperta
verificando invece di supporre.

**Perché.** È l'unico taglio grosso sul ruolo più costoso che **non tocca la sua memoria**, quindi non
mette a rischio il suo giudizio. E il principio: chi decide va interrotto per una decisione, non per
un aggiornamento.

**Il prezzo pagato.** Un rapporto ordinario può aspettare fino a cinque minuti prima di essere letto.
Il tetto è cinque minuti e non di più per una ragione precisa: a otto minuti l'app comincia a
sollecitare il supervisore perché "deve un verdetto" — solleciterebbe per un rapporto che essa stessa
sta trattenendo. Una configurazione oltre il tetto viene rifiutata, e il rifiuto **viene scritto nel
registro** con entrambi i numeri.

**Due difetti che questa voce deve raccontare, perché sono stati introdotti e corretti in giornata.**
La prima versione funzionava **una volta sola**: il suo cronometro non veniva mai azzerato, quindi dal
secondo rapporto in poi il supervisore si svegliava come prima. La seconda lo azzerava quando un turno
*partiva* — ma un turno che parte e **fallisce** non consuma i messaggi, e allora il tentativo
successivo si prendeva una finestra nuova: con tre tentativi un rapporto poteva aspettare venti minuti
invece di cinque. Adesso il cronometro si spende **dove i messaggi lasciano davvero il canale**. Il
difetto era lo stesso, spostato di confine: sono i posti dove una cosa "quasi sempre accade" a
nascondere i casi in cui non accade.

**Dove.** `stage/4h-wakeup-digest`.

### Il revisore ha il suo modello, e la firma di un messaggio non dà privilegi

**Com'era.** La configurazione aveva un modello per ruolo, ma non uno per il revisore né per il solo:
entrambi ereditavano quello dell'implementatore. Dire "implementatore economico, revisore grosso" era
letteralmente inesprimibile.

**Cos'è adesso.** Ogni ruolo ha la sua chiave, lette tutte da un unico punto, e una chiave assente
continua a seguire quella dell'implementatore — quindi una configurazione esistente si comporta
esattamente come prima.

**Cosa è stato tentato e ritirato, che è la parte che vale.** Insieme a questo era stata costruita una
cosa più ambiziosa: il supervisore scriveva `MODEL: sonnet` nell'incarico e il bridge la leggeva. È
stata **cancellata** dopo che una revisione avversaria ha dimostrato che si appoggiava a una firma
falsificabile: l'autore di un messaggio, in questo sistema, è solo testo, e un membro che cita un
messaggio nel proprio rapporto — cosa che il protocollo gli chiede — può far comparire una firma che
non è sua. Un membro si sarebbe potuto assegnare il modello costoso. Il privilegio passerà per il
foglietto di richiesta che il supervisore già usa: lì conta **dove** lo scrivi, non cosa ci scrivi.

**Il principio, oltre questo caso.** Un privilegio non si concede sulla base di un'affermazione che
chi ne beneficia può scrivere da sé. Dove serve autorizzare, l'autorizzazione deve venire da qualcosa
che il richiedente non controlla.

**Un difetto corretto nella stessa giornata.** Rendendo tollerante la lettura dei valori di
configurazione (un numero scritto dove va una parola non deve impedire l'avvio dell'app) era stato
riaperto, da un'altra porta, un difetto chiuso mesi prima: il motore del piano scivolava sul
comportamento predefinito **in silenzio**. Ora un valore del tipo sbagliato arriva a chi lo deve
rifiutare, e viene rifiutato ad alta voce.

**Dove.** `stage/4i-model-per-task`.

### Un'impostazione che si legge una volta sola è un'impostazione che mente

**Com'era.** Il modello del supervisore generale veniva fissato alla prima registrazione della
sessione e non veniva più riletto. La configurazione diceva un modello, la sessione ne stava usando
un altro dal giorno prima, e nessun riavvio poteva cambiarla.

**Cos'è adesso.** Il modello viene riconciliato con la configurazione all'inizio di ogni turno, cioè
nel punto in cui serve davvero, e la riconciliazione viene scritta nel registro.

**Perché era sfuggito.** Tutte le altre sessioni ripassano da un punto che rilegge la
configurazione — un membro lo fa quando viene aggiunto o fatto ripartire — e il supervisore generale
è l'unico che non ci ripassa mai, apposta: non c'è un processo da sorvegliare, quindi non viene mai
fatto ripartire. Un caso speciale corretto da una parte lascia scoperta la lettura dall'altra.

**Il principio, che vale oltre questo caso.** Un valore va riconciliato nel punto in cui produce
effetto, non nel punto in cui è comodo leggerlo. Altrimenti il sistema mostra un'impostazione e ne
applica un'altra, e chi la cambia crede di aver cambiato qualcosa.

**Dove.** `stage/4f-general-model-refresh`.

---

## 5. Onestà: che cosa il sistema può dire di sapere

Questa sezione è la più importante da leggere, perché contiene le regole che valgono anche fuori da
questo progetto.

### Un controllo che non trova ciò che deve controllare non permette tutto in silenzio

**Com'era.** Nove punti in sei script davano per scontato dove stessero le cartelle della
supervisione, ignorando l'indicazione ricevuta all'avvio. Se il servizio era stato avviato altrove,
ogni controllo cercava un percorso inesistente, non trovava niente, e **permetteva tutto**. Senza un
errore da nessuna parte.

**Cos'è adesso.** Ogni punto legge l'indicazione ricevuta, e un test elenca gli script leggendoli dal
disco invece che da una lista scritta a mano, così uno aggiunto domani viene preso lo stesso.

**Perché.** È peggio di una regola non applicata, perché il protocollo dice alla sessione il
contrario: «l'app ti ferma». Su quelle macchine quella frase era semplicemente falsa, e una sessione
onesta che ci crede si comporta come se fosse protetta.

**Dove.** commit diretto su `ours/integration` (`b828090`).

### Sei test rossi per sempre diventano salti onesti

**Com'era.** Sei test rossi su macOS da sempre, con una diagnosi scritta accanto che era pure
sbagliata (non era una questione di permessi).

**Cos'è adesso.** Provocano un guasto tenendo un file aperto: è una cosa che su Windows fa fallire
la scrittura, mentre su macOS e Linux il sistema non consulta affatto i file aperti dagli altri, e
quindi non c'è proprio niente da provocare. Adesso il test **prova la macchina** invece di leggerne
il nome, e dove il meccanismo non esiste salta dicendo la ragione vera. Dove esiste, verifica tutto
quello che verificava prima.

**Perché.** Una suite permanentemente rossa è il posto dove si nasconde un guasto vero: si impara a
dire «ah, sono quei sei» e si lascia passare il settimo. Da qui la regola di questo fork: si
confrontano i **nomi**, non il numero.

**Dove.** commit diretto su `ours/integration` (`b2dde29`).

### I test non si rubano più le prove a vicenda

**Com'era.** Le diagnostiche di un lock finivano in un raccoglitore unico per tutto il processo, e
ogni test che avviava il motore se lo riprendeva. Chi stava raccogliendo si vedeva sparire le prove
a metà, e il furto compariva come un fallimento suo, sotto un nome che non c'entrava niente. Cinque
esecuzioni complete rosse su sei.

**Cos'è adesso.** Ogni test raccoglie sul proprio flusso; la produzione è intatta. Zero fallimenti
su dieci esecuzioni complete.

**Perché conta.** Un rosso intermittente rende **inaffidabile ogni verifica di merge**: non si sa più
se si sta guardando un difetto nuovo o il solito rumore. E un caso è istruttivo: il finto Claude
usato nei test andava in crash quando due copie scrivevano nello stesso registro, e il crash faceva
riuscire un membro che il test voleva veder rifiutato — cioè prendeva esattamente la forma di un
guasto nel limite d'uso, in una prova dove il limite non era mai stato raggiunto. Il sintomo
accusava lo strato sbagliato.

**Una cosa provata e rifiutata**, scritta perché nessuno la riprovi: mettere tutte le trentuno classi
in un gruppo esclusivo. Misurato: 6 minuti e 26 contro 1 minuto e 35. Un costo di quattro volte su
ogni esecuzione, per sempre, per rendere deterministico un test.

**Dove.** `stage/7l-diagnostics-sink`, `stage/7m-flaky-resume-test`.

### La suite gira su tempi finti

**Com'era.** I test del bridge aspettavano tempi veri: 2583 test impiegavano 107 secondi di orologio
per 27 di calcolo. Uno solo aspettava 33 secondi veri.

**Cos'è adesso.** I tre valori di tempo del ciclo si iniettano; in produzione valgono esattamente i
numeri che erano scritti prima, identici. La suite passa da 107 secondi a 37. Nessun test è stato
cancellato, saltato o indebolito.

**Perché.** Una suite lenta si esegue di meno, e una suite che si esegue di meno non è una rete.

**Dove.** `stage/7d-fake-clock-tests`.

### Le regole del messaggio al proprietario sono controllate, non sperate

**Com'era.** Un conteggio meccanico del prompt del supervisore: 229 regole imperative e 14 coppie che
non si possono rispettare entrambe. Vince sempre quella più in basso, perché è l'ultima che il
modello ha letto. Due esempi: «parti dalla decisione» a riga 116 contro un accuso di ricezione
obbligatorio 396 righe più sotto — che è letteralmente la cosa che la prima regola vieta; e «una
domanda chiude il tuo turno» contro «costruiscilo e chiedi di passaggio» 783 righe più sotto. Il
modello soddisfa entrambe chiedendo in mezzo alla prosa senza il marcatore: così non accende
nessuna spia, non risulta da nessuna parte, e nessuno la insegue. Ecco come si perdono le domande.

**Cos'è adesso.** Un controllo automatico verifica **solo ciò che una macchina può stabilire**: due
domande in una voce sola, prosa sotto la domanda, opzioni senza domanda, un accuso di ricezione al
posto della risposta, un percorso di file o una traccia di errore mandati a chi non legge codice, la
lunghezza. E riporta senza ingoiare: il messaggio arriva comunque al proprietario, e la sessione si
vede dire nel proprio canale che cosa c'era che non andava.

**Perché così e non di più.** Un controllo che scarta il messaggio trasformerebbe un errore di forma
nella perdita di una risposta, che è il guaio peggiore di questo sistema. E «rispondi in modo
completo» o «scrivi in parole semplici» sono regole vere ma sono deliberatamente escluse: un
controllo che tira a indovinare insegna alla sessione a scrivere per il controllo.

**Dove.** commit diretto su `ours/integration` (`86d695e`).

### L'applicazione Windows si compila su Windows

**Com'era.** Il progetto dell'interfaccia gira solo su Windows, e le macchine su cui si scrive questo
repo non riescono nemmeno a provarci. Il 2026-09-09 sono finiti in un merge quattro file modificati
alla cieca, con scritto nel commit «manca ancora una compilazione Windows».

**Cos'è adesso.** L'integrazione continua la fa a ogni push, e compila **solo** ciò che le altre
macchine non possono: prima l'app da sola, così un errore la nomina, poi tutto il resto.

**Perché.** «Manca ancora una verifica» dentro un messaggio di commit non è una verifica: è un debito
che qualcuno deve ricordarsi di pagare. Se una macchina può pagarlo da sola, lo paga la macchina.

**Dove.** commit diretto su `ours/integration` (`a2adb76`).

### La revisione avversaria dopo ogni tornata

**Cos'è.** Dopo un gruppo di modifiche, revisori indipendenti in copie di lavoro separate, con
l'obbligo di **dimostrare** ogni difetto con una prova eseguibile, non di segnalarlo.

**Perché.** Su tre modifiche che cambiavano comportamento vivo, ha trovato difetti veri in tutte e
tre — quelli raccontati qui sopra nell'appuntamento del limite d'uso, nel turno di chiusura e nella
voce troncata. Nessuno di quei difetti era visibile da una suite verde, e tutti e tre sarebbero
arrivati al proprietario nella forma «l'app si è mangiata il mio messaggio». Non è cerimonia: è
l'unico controllo che ha trovato qualcosa.

**Dove.** raccontata in `docs/superpowers/specs/2026-09-09-report-lavoro-e-runbook.md`.

---

## 6. Che cosa stiamo facendo adesso

Questa sezione è l'unica che parla al futuro, quindi invecchia in fretta: **aggiornata al
2026-09-09, notte**. Chi la legge dopo la confronti con `docs/superpowers/specs/`, dove il piano vive.

**Dov'è il costo che resta.** Le sessioni fresche hanno tolto di mezzo il contesto trascinato da un
turno all'altro; adesso la spesa è concentrata **dentro** i turni lunghi, quelli che arrivano fino
alla scadenza. Il prossimo intervento è un consiglio dato a metà turno — «arriva a un punto stabile,
salva, racconta» — mentre la scadenza resta la rete dura di prima. È un suggerimento, non un
divieto: chi lavora deve poter decidere che il punto stabile è più in là.

**Le altre cose in fila:** un riassunto di risveglio per il supervisore, così i messaggi meccanici
non gli comprano un turno intero; il modello scelto direttamente nell'incarico invece che nella
configurazione, e la chiave del revisore separata da quella dell'implementatore, che oggi sono la
stessa; e infine il supervisore fresco anche lui, con il suo pacchetto — che è già costruito e sta
lì spento.

**Cose aperte, dette perché non sembrino risolte.**
- I guadagni misurati sono **misurati prima della messa in produzione**: sul server, dopo, non sono
  ancora stati rifatti. Sono attesi, non verificati.
- Il file di istruzioni del progetto principale è stato modificato benché sia territorio di chi ha
  scritto l'originale: andrà risolto al prossimo allineamento.

**Le tre modifiche di quella notte sono state fermate, corrette e consegnate il giorno dopo.** Il
consiglio a metà turno, il riassunto di risveglio del supervisore e la chiave del revisore sono state
scritte la notte del 9 settembre, **rotte una per una da una revisione avversaria indipendente** con
una prova per ogni difetto, corrette, **rotte di nuovo** dalla seconda revisione — che ha trovato una
regressione introdotta dalla prima correzione — corrette ancora, e messe in produzione il 10 settembre
alle 11:43. Le tre voci qui sopra raccontano cosa fanno e cosa è costato; i difetti con le loro sonde
stanno in `docs/superpowers/specs/2026-09-09-review-findings-4g-4h-4i.md`.

Vale la pena dire *come* erano rotte la prima volta, perché sono tre volte lo stesso errore in tre
posti diversi: **una funzione che non funziona affatto, dietro una suite verde.** Il consiglio a metà
turno non arrivava mai, perché due informazioni lette dal messaggio della CLI si fondevano in una — e
tutti i suoi ventuno test passavano perché il messaggio finto che usavano conteneva un campo che
quello vero non manda. Il riassunto di risveglio funzionava una volta sola, perché il suo cronometro
non veniva mai azzerato. La scelta del modello si appoggiava alla firma di un messaggio, che in questo
sistema è solo testo.

**La lezione, che conta più delle tre funzioni.** Cinque revisioni su cinque hanno trovato difetti
reali *dopo* che qualcuno aveva letto il diff e visto la suite verde — e la seconda tornata ha trovato
difetti nelle correzioni della prima, compresa una regressione che spostava un difetto da un confine
raro a uno frequente. Quindi: nessuna modifica che cambia comportamento vivo va integrata senza la sua
revisione avversaria, chi la commissiona non è chi la certifica, e **una correzione va revisionata come
una modifica** — è codice nuovo, quindi superficie nuova. E un test si giudica da una domanda sola:
passerebbe anche se la funzione fosse spenta? Se sì, non fissa nulla.

**Una lezione di processo, che vale più di una funzione.** Il 2026-09-09 due filoni di lavoro sono
andati avanti in parallelo sugli stessi file, e **la stessa funzione è stata costruita due volte** —
il turno di chiusura, da due parti che non sapevano l'una dell'altra. Ha vinto la versione migliore
e l'altra è stata tenuta solo come traccia, ma sono ore buttate. Da lì la regola: chi sta per
mettere mano a un pezzo lo dichiara prima, in un file che gli altri leggono, con l'elenco dei file
che intende toccare. Il costo del coordinamento è di gran lunga inferiore al costo di scoprirlo
dopo.
