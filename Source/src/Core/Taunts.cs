using System;
using System.Collections.Generic;
using System.IO;
using Aquiris.HorizonChase.Data;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// The speech bubbles. The game localises "speech_&lt;car id&gt;_&lt;type&gt;&lt;n&gt;" (types: random1-5, nitro1-2,
    /// crash1-3, overtake1-3, out_of_nitros, first_place). Cars the game does not know (the F1 grid, or
    /// anything added later) have no such lines, so the bubble showed the raw key: "*speech_..._random1*".
    ///
    /// Lines come from plain text files, one per car, one per campaign, plus a default:
    ///   Game\HCT15Content\cars\taunts.&lt;car id&gt;.txt            one car's own voice, used in EVERY campaign
    ///                                                        (the taxi and the micro talk flaite wherever they
    ///                                                        race); taunts.&lt;car id&gt;.&lt;lang&gt;.txt to translate it
    ///   Game\HCT15Content\campaigns\&lt;folder&gt;\taunts.txt        the campaign's lines (all languages)
    ///   Game\HCT15Content\campaigns\&lt;folder&gt;\taunts.&lt;lang&gt;.txt the same in one game language ("es", "pt", "fr",
    ///                                                        "de", "it"...): used instead of taunts.txt while
    ///                                                        the game runs in that language
    ///   Game\HCT15Content\taunts.txt                        (used when no campaign file exists)
    /// The Grand Prix 2026 campaign ships English pit radio (taunts.txt) plus es/pt/fr/de/it versions, so the
    /// bubbles follow the language chosen in Options. The Chile campaign is Chilean Spanish on purpose in
    /// every language - add a taunts.en.txt there to change that.
    /// Format: "[random]" / "[nitro]" / "[out_of_nitros]" / "[crash]" / "[overtake]" / "[first_place]" section
    /// headers, one line per bubble, "#" starts a comment, blank lines ignored, UTF-8. Written with the
    /// defaults on first run; edit freely.
    /// Who gets them: the mod's own cars always (their campaign's file, F1 grid -> f1_2026\taunts.txt);
    /// EVERY car while a mod campaign with a taunts.txt runs (the retail lines are overridden), or always
    /// with "ChileanTaunts": "all" (then the default file). Lines are picked per car and slot, so each car
    /// keeps its own set for the whole session. Refresh() runs again whenever the language changes.
    /// </summary>
    public static class Taunts
    {
        private static readonly string[] Types = { "random", "nitro", "out_of_nitros", "crash", "overtake", "first_place" };
        private static readonly int[] Slots = { 5, 2, 0, 3, 3, 0 };   // 0 = a single key without a number
        private static readonly string[] F1Languages = { "es", "pt", "fr", "de", "it" };

        private class Lines
        {
            public string Path;
            public Dictionary<string, List<string>> ByType = new Dictionary<string, List<string>>();
            public int Count { get { int n = 0; foreach (var l in ByType.Values) n += l.Count; return n; } }
        }

        private static readonly Dictionary<string, Lines> _files = new Dictionary<string, Lines>();   // "folder|lang" -> lines
        private static readonly Dictionary<string, string> _override = new Dictionary<string, string>();
        private static bool _all;
        private static string _allFrom = "";

        // ------------------------------------------------------------------ defaults

        private static string Header(string what)
        {
            return "# HCT 1.6 - speech bubbles " + what + "\r\n" +
                   "# Sections: [random] anything, [nitro] when boosting, [out_of_nitros], [crash] after a hit, [overtake], [first_place]\r\n" +
                   "# One line per bubble. '#' starts a comment. UTF-8. Each car gets its own fixed set from these.\r\n" +
                   "# taunts.<lang>.txt beside this file is used while the game runs in that language (es, pt, fr, de, it...).\r\n\r\n";
        }

        private static string Join(string section, params string[] lines) { return "[" + section + "]\r\n" + string.Join("\r\n", lines) + "\r\n\r\n"; }

        private static string ChileanDefaults()
        {
            return Header("for the Chile campaign (Chilean Spanish, strong language as asked; the same in every game language)") +
                   "# Sources: baselang.com (47 Chilean slang words), trufluency.com (15 Chilean swear words), speakinglatino.com\r\n\r\n" +
                   "[random]\r\n¡Ya po, weón!\r\n¡Cachái o no cachái?\r\n¡Puta la weá!\r\n¡Chucha!\r\n¡Conchetumare!\r\n¡Ándate a la chucha!\r\n¡Ándate a la cresta!\r\n¡Sale, aweonao!\r\n¡Mira este gil!\r\n¡Qué lata, weón!\r\n¡Fome!\r\n¡Al tiro!\r\n¡Bacán!\r\n¡La raja!\r\n¡Filo!\r\n¡Piola no más!\r\n¡Ni ahí!\r\n¡Quiubo, cabros!\r\n¡Manso taco!\r\n¡Buena onda!\r\n¡Cuático!\r\n¡Pura cháchara!\r\n¡Anda a llorarle a tu mamá!\r\n¡Sapo culiao!\r\n¡Qué chucha hacís!\r\n¡Estás pa' la corneta!\r\n¡Te fuiste al chancho!\r\n¡Saco de weas!\r\n¡Pelao!\r\n¡Guatón!\r\n¡Flaite!\r\n¡Chanta!\r\n¡No seas quemao!\r\n¡Pa' la casa, gil!\r\n¡Chao pescao!\r\n¡Ándate a freír monos!\r\n¡Manda cambio!\r\n¡Apaña, po!\r\n¡Vamos que se puede!\r\n¡Ya, po, apúrate!\r\n¡Estoy chato!\r\n¡Ni un brillo!\r\n¡La firme!\r\n¡Pícate!\r\n¡Jetón!\r\n¡Fresco!\r\n¡Ahueonao!\r\n¡Culiao!\r\n¡Saco wea!\r\n¡Pergüétano!\r\n¡Hijoeputa!\r\n¡Condoro tuyo!\r\n¡Dejá de dar jugo!\r\n¡Tremendo weón!\r\n¡Ojo con el piño!\r\n¡Mueve la cachaña!\r\n¡Sale de ahí, po!\r\n¡Weá!\r\n¡Caleta de gente aquí!\r\n¡Vamos con to'!\r\n¡A la punta del cerro!\r\n¡Puro atao!\r\n¡Me chorié!\r\n¡Estás curao o qué!\r\n¡Quemao!\r\n\r\n" +
                   "[nitro]\r\n¡Con to', weón!\r\n¡Vamos, po!\r\n¡A todo chancho!\r\n¡Al lote!\r\n¡Sécate!\r\n¡Come polvo, gil!\r\n¡Fuaaa!\r\n¡La raja!\r\n\r\n" +
                   "[out_of_nitros]\r\n¡Puta, se me acabó!\r\n¡Chucha, sin nitro!\r\n¡Ando pato de nitro!\r\n¡Qué lata!\r\n¡Condoro!\r\n\r\n" +
                   "[crash]\r\n¡Conchetumare!\r\n¡Me sacaste la chucha!\r\n¡Me sacaste la cresta!\r\n¡Aweonao!\r\n¡Culiao, mirá pa' dónde vas!\r\n¡Puta la weá!\r\n¡Chucha!\r\n¡Qué weá!\r\n¡Sale, saco de weas!\r\n¡Ahueonao!\r\n\r\n" +
                   "[overtake]\r\n¡Te pasé, po!\r\n¡Chao pescao!\r\n¡Come polvo!\r\n¡Sécate, weón!\r\n¡Quedaste pa' la corneta!\r\n¡Pa' la casa, gil!\r\n¡Anda a llorar, po!\r\n¡Ni ahí contigo!\r\n¡Manda cambio!\r\n\r\n" +
                   "[first_place]\r\n¡La raja, weón!\r\n¡Bacán!\r\n¡Primero, po!\r\n¡Vamos que se puede!\r\n¡Tremendo!\r\n¡Chúpate esa!\r\n¡Nadie me pilla!\r\n";
        }

        /// <summary>
        /// The street cars talk like the street. One file per car under Game\HCT15Content\cars\, so the taxi
        /// and the micro keep this voice in every campaign; null for any other car, which then follows the
        /// campaign it is racing in. Flaite register, aimed at the driver who just cut you off - the same
        /// strength as the Chile campaign file, no line aimed at what somebody is rather than how they drive.
        /// </summary>
        private static string CarDefaults(string carId)
        {
            string head = "# HCT 1.6 - speech bubbles for one car: " + carId + "\r\n" +
                          "# This file wins over the campaign's taunts.txt, so this car keeps its own voice everywhere.\r\n" +
                          "# Sections: [random] anything, [nitro] when boosting, [out_of_nitros], [crash] after a hit, [overtake], [first_place]\r\n" +
                          "# One line per bubble. '#' starts a comment. UTF-8. Each car gets its own fixed set from these.\r\n" +
                          "# taunts." + carId + ".<lang>.txt beside this file is used while the game runs in that language.\r\n\r\n";
            switch (carId)
            {
                case "cl_taxi":
                    // 50 lines of a Santiago taxi driver at his most flaite (2026-09-23)
                    return head +
                           Join("random", "¡Qué te pasa, conchetumare!", "¡Córrete, weón culiao!", "¡Aprende a manejar, saco 'e weas!",
                                "¡Erís más lento que la micro, aweonao!", "¡Ya po, weón, ¿vai a avanzar o no?!",
                                "¡Pa' qué te dieron licencia, gil culiao!", "¡La pista no es tuya, ctm!",
                                "¡Estái más perdío que el teniente Bello, weón!", "¡Andái puro weando, culiao!",
                                "¡Me tenís chato, weón!", "¡Sácate de ahí, pastel!", "¡Manejái como el forro!",
                                "¡Oe, pajarón, mira pa' delante!", "¡Aweonao, tenís el tarro en neutro!",
                                "¡Qué onda, perkin, ¿te compraste la calle?!", "¡Te voy a sacar la cresta, weón!",
                                "¡Puro color tirái, chanta culiao!", "¡Anda a acostarte, pollo!",
                                "¡Ni el Transantiago es tan lento, weón!", "¡Me caís como las weas, conchetumare!") +
                           Join("nitro", "¡Agárrate, que voy a la chucha!", "¡A la cresta con el taxímetro!", "¡Pa' la pampa, weón!",
                                "¡Voy más rápido que la cresta!", "¡Suelta la weá, que nos vamos!", "¡Chao con tu weá!") +
                           Join("out_of_nitros", "¡Chucha, se me acabó la weá!", "¡Puta la cagá, sin nitro!", "¡Me quedé seco, conchetumare!",
                                "¡Mierda, ando a pata!") +
                           Join("crash", "¡Conchetumare, me rayaste el taxi!", "¡Me sacaste la cresta, culiao!",
                                "¡Vo' me pagái el choque, weón!", "¡Puta la weá, mi parachoque!",
                                "¡Weón culiao, ¿no veí o qué?!", "¡Te voy a sacar la chucha, pastel!",
                                "¡Qué hací, saco 'e weas!", "¡Me cagaste la pega, ctm!") +
                           Join("overtake", "¡Chao, gil culiao!", "¡Te pasé por la chucha!", "¡Quedaste pa' la cagá, weón!",
                                "¡Aprende, aweonao!", "¡Te dejé botao como colilla!", "¡Mírame la patente, gil!",
                                "¡Por la cresta que erís lento!", "¡Hasta la vista, pajarón!") +
                           Join("first_place", "¡La raja, conchetumare!", "¡Soy el más vio de Santiago, weón!", "¡Chúpense esa, culiaos!",
                                "¡Primero, y el resto que se vaya a la chucha!");

                case "cl_micro":
                    return head +
                           Join("random", "¡Corre la pasá, weón!", "¡Súbete atrás po, gil!",
                                "¡Esta micro no para, culiao!", "¡Yo llevo cuarenta y tú ni uno!",
                                "¡Andái más penca que paradero sin techo!", "¡Sale de la pista, aweonao!",
                                "¡Con boleto o sin boleto, igual te paso!", "¡Ese auto es de juguete, weón!",
                                "¡Yo manejo con la rodilla, chanta!", "¡Te falta calle, cabrito!",
                                "¡Mírate el fierro, rasca entero!", "¡Pura lata y na' de motor!",
                                "¡Quedaste en el paradero, gil!", "¡Ni pa' repuesto servís, culiao!",
                                "¡Longi, mira pa'l frente!", "¡Esta cuma le gana a tu nave!",
                                "¡Toco la bocina y te corrís, po!", "¡Ándate a la cresta con tu carrito!",
                                "¡La micro manda en la calle, weón!", "¡Estái más quemao que motor de micro vieja!",
                                "¡Puro humo tirái, aweonao!", "¡Te paso por fuera, gil!") +
                           Join("nitro", "¡Agárrense po!", "¡Esta micro vuela, weón!", "¡Con to', culiao!",
                                "¡Ahí va la corrida!", "¡Sécate, gil!", "¡Fuaaa!") +
                           Join("out_of_nitros", "¡Puta, quedé en pana!", "¡Se acabó el diésel, weón!",
                                "¡Chucha, sin nitro po!", "¡Ando pato!") +
                           Join("crash", "¡Conchetumare, la micro!", "¡Se me cayeron los pasajeros, culiao!",
                                "¡Mira pa' dónde vai, aweonao!", "¡Me sacaste la chucha!", "¡Ahí va la lata, gil!",
                                "¡Puta la weá!", "¡Andái sin licencia o qué!", "¡Esta micro pesa, weón, cuidao!",
                                "¡Te la cobro en el paradero!") +
                           Join("overtake", "¡Corre la pasá, gil!", "¡Chao pescao!", "¡Come polvo, weón!",
                                "¡Te dejé en el paradero!", "¡Pa' la casa, chanta!", "¡Sécate, aweonao!",
                                "¡La micro pasa por fuera!", "¡Anda a llorar po!", "¡Ni ahí contigo, culiao!") +
                           Join("first_place", "¡Primera la micro po!", "¡La raja, weón!", "¡Chúpate esa, gil!",
                                "¡Ganó la cuma!", "¡Nadie le gana a la micro!", "¡Vamos con to'!");
            }
            return null;
        }

        private static string F1Defaults()
        {
            return Header("for the Grand Prix 2026 campaign (pit-radio English)") +
                   Join("random", "Box, box!", "Copy that.", "Mamma mia!", "Push now, push!", "DRS enabled.", "Plan B, plan B!", "We are checking.", "Tyres are gone, mate.", "Blue flags! Blue flags!", "Mind the track limits.", "Full send.", "I'm losing power!", "Let's go!", "Come on, come on!", "Tell him to move!", "I need clean air.", "How's the gap?", "Save fuel? Are you serious?", "No, no, no, no!", "What's the plan?", "Keep it clean out there.", "This is how you drive, mate!", "Everything is under control.", "What tyre is he on?", "Sector one purple!", "Push like hell!", "Radio check.") +
                   Join("nitro", "DRS open, push!", "Overtake mode on!", "Deploy, deploy!", "Full power now!", "Quali mode, go!") +
                   Join("out_of_nitros", "No more deploy.", "Battery is empty.", "Save the energy.", "Lift and coast.", "We have nothing left.") +
                   Join("crash", "He hit me!", "Where is he going?!", "What a dangerous driver!", "Unbelievable!", "Are you kidding me?!", "I have damage!", "Front wing is gone!", "He pushed me off!", "That was not my fault!", "Check the car, please.") +
                   Join("overtake", "Bye bye!", "Too easy!", "The undercut worked!", "See you later!", "Get in there!", "Good move, mate.", "Thank you, next!", "That's how it's done.", "DRS train is over.") +
                   Join("first_place", "P1! P1!", "We did it, guys!", "Yes! Yes! Yes!", "Leading the race!", "Get in there!", "What a drive!");
        }

        /// <summary>The F1 pit radio in one of the game's languages (the iconic lines stay in their original words).</summary>
        private static string F1Defaults(string lang)
        {
            switch (lang)
            {
                case "es":
                    return Header("for the Grand Prix 2026 campaign - Spanish (used while the game runs in Spanish)") +
                           Join("random", "¡Box, box!", "Recibido.", "¡Mamma mia!", "¡Aprieta ahora, aprieta!", "DRS activado.", "¡Plan B, plan B!", "Lo estamos revisando.", "Las gomas están muertas.", "¡Banderas azules! ¡Banderas azules!", "Cuidado con los límites de pista.", "A fondo.", "¡Estoy perdiendo potencia!", "¡Vamos!", "¡Vamos, vamos!", "¡Dile que se mueva!", "Necesito aire limpio.", "¿Cómo va la diferencia?", "¿Ahorrar combustible? ¿En serio?", "¡No, no, no, no!", "¿Cuál es el plan?", "Mantén la limpieza ahí fuera.", "¡Así se maneja, compadre!", "Todo bajo control.", "¿Qué gomas lleva?", "¡Sector uno en morado!", "¡Aprieta con todo!", "Prueba de radio.") +
                           Join("nitro", "¡DRS abierto, aprieta!", "¡Modo adelantamiento activado!", "¡Despliega, despliega!", "¡Potencia máxima ahora!", "¡Modo clasificación, vamos!") +
                           Join("out_of_nitros", "No queda energía.", "La batería está vacía.", "Ahorra energía.", "Levanta y deja rodar.", "No nos queda nada.") +
                           Join("crash", "¡Me chocó!", "¡¿Adónde va?!", "¡Qué piloto tan peligroso!", "¡Increíble!", "¡¿Es en serio?!", "¡Tengo daños!", "¡Perdí el alerón delantero!", "¡Me sacó de la pista!", "¡No fue mi culpa!", "Revisen el auto, por favor.") +
                           Join("overtake", "¡Chao chao!", "¡Demasiado fácil!", "¡El undercut funcionó!", "¡Hasta luego!", "¡Métete ahí!", "Buena maniobra, compadre.", "¡Gracias, el siguiente!", "Así se hace.", "Se acabó el tren de DRS.") +
                           Join("first_place", "¡P1! ¡P1!", "¡Lo logramos, chicos!", "¡Sí! ¡Sí! ¡Sí!", "¡Liderando la carrera!", "¡Métete ahí!", "¡Qué carrera!");
                case "pt":
                    return Header("for the Grand Prix 2026 campaign - Portuguese (used while the game runs in Portuguese)") +
                           Join("random", "Box, box!", "Entendido.", "Mamma mia!", "Acelera agora, acelera!", "DRS ativado.", "Plano B, plano B!", "Estamos verificando.", "Os pneus acabaram, cara.", "Bandeira azul! Bandeira azul!", "Cuidado com os limites da pista.", "Tudo ou nada.", "Estou perdendo potência!", "Vamos!", "Vamos, vamos!", "Mande ele sair da frente!", "Preciso de ar limpo.", "Como está a diferença?", "Economizar combustível? Sério?", "Não, não, não, não!", "Qual é o plano?", "Mantenha limpo aí.", "É assim que se pilota, cara!", "Tudo sob controle.", "Que pneu ele está usando?", "Setor um roxo!", "Acelera com tudo!", "Teste de rádio.") +
                           Join("nitro", "DRS aberto, acelera!", "Modo ultrapassagem ligado!", "Descarrega, descarrega!", "Potência máxima agora!", "Modo classificação, vai!") +
                           Join("out_of_nitros", "Sem mais energia.", "A bateria está vazia.", "Economize a energia.", "Solta e deixa rolar.", "Não sobrou nada.") +
                           Join("crash", "Ele me bateu!", "Para onde ele vai?!", "Que piloto perigoso!", "Inacreditável!", "Está brincando comigo?!", "Tenho danos!", "A asa dianteira já era!", "Ele me jogou para fora!", "Não foi culpa minha!", "Verifiquem o carro, por favor.") +
                           Join("overtake", "Tchau, tchau!", "Fácil demais!", "O undercut funcionou!", "Até mais!", "Vai pra dentro!", "Boa manobra, cara.", "Obrigado, próximo!", "É assim que se faz.", "O trem de DRS acabou.") +
                           Join("first_place", "P1! P1!", "Conseguimos, pessoal!", "Sim! Sim! Sim!", "Liderando a corrida!", "Vai pra dentro!", "Que pilotagem!");
                case "fr":
                    return Header("for the Grand Prix 2026 campaign - French (used while the game runs in French)") +
                           Join("random", "Box, box !", "Bien reçu.", "Mamma mia !", "Pousse maintenant, pousse !", "DRS activé.", "Plan B, plan B !", "On vérifie.", "Les pneus sont morts, mon pote.", "Drapeaux bleus ! Drapeaux bleus !", "Attention aux limites de piste.", "À fond.", "Je perds de la puissance !", "Allez !", "Allez, allez !", "Dis-lui de bouger !", "J'ai besoin d'air propre.", "L'écart, c'est combien ?", "Économiser le carburant ? Sérieux ?", "Non, non, non, non !", "C'est quoi le plan ?", "Reste propre là-dehors.", "C'est comme ça qu'on pilote !", "Tout est sous contrôle.", "Il est sur quels pneus ?", "Secteur un en violet !", "Pousse à fond !", "Test radio.") +
                           Join("nitro", "DRS ouvert, pousse !", "Mode dépassement activé !", "Déploie, déploie !", "Pleine puissance !", "Mode quali, go !") +
                           Join("out_of_nitros", "Plus de déploiement.", "La batterie est vide.", "Économise l'énergie.", "Lève le pied et laisse rouler.", "On n'a plus rien.") +
                           Join("crash", "Il m'a percuté !", "Où est-ce qu'il va ?!", "Quel pilote dangereux !", "Incroyable !", "C'est une blague ?!", "J'ai des dégâts !", "L'aileron avant est parti !", "Il m'a poussé hors piste !", "Ce n'était pas ma faute !", "Vérifiez la voiture, s'il vous plaît.") +
                           Join("overtake", "Bye bye !", "Trop facile !", "L'undercut a marché !", "À plus tard !", "Vas-y, fonce !", "Belle manœuvre.", "Merci, au suivant !", "C'est comme ça qu'on fait.", "Fini le train DRS.") +
                           Join("first_place", "P1 ! P1 !", "On l'a fait, les gars !", "Oui ! Oui ! Oui !", "En tête de la course !", "Vas-y, fonce !", "Quelle course !");
                case "de":
                    return Header("for the Grand Prix 2026 campaign - German (used while the game runs in German)") +
                           Join("random", "Box, box!", "Verstanden.", "Mamma mia!", "Jetzt pushen, push!", "DRS aktiviert.", "Plan B, Plan B!", "Wir prüfen das.", "Die Reifen sind hin, Kumpel.", "Blaue Flaggen! Blaue Flaggen!", "Achte auf die Streckenlimits.", "Volle Attacke.", "Ich verliere Leistung!", "Los geht's!", "Komm schon, komm schon!", "Sag ihm, er soll Platz machen!", "Ich brauche freie Luft.", "Wie groß ist der Abstand?", "Sprit sparen? Ist das dein Ernst?", "Nein, nein, nein, nein!", "Was ist der Plan?", "Bleib sauber da draußen.", "So fährt man, Kumpel!", "Alles unter Kontrolle.", "Auf welchen Reifen ist er?", "Sektor eins lila!", "Push wie verrückt!", "Funkcheck.") +
                           Join("nitro", "DRS offen, push!", "Überholmodus an!", "Deploy, deploy!", "Volle Leistung jetzt!", "Quali-Modus, los!") +
                           Join("out_of_nitros", "Kein Deploy mehr.", "Die Batterie ist leer.", "Spar die Energie.", "Lift and coast.", "Wir haben nichts mehr.") +
                           Join("crash", "Er hat mich getroffen!", "Wo will er denn hin?!", "Was für ein gefährlicher Fahrer!", "Unglaublich!", "Soll das ein Witz sein?!", "Ich habe Schaden!", "Der Frontflügel ist weg!", "Er hat mich rausgedrängt!", "Das war nicht meine Schuld!", "Prüft bitte das Auto.") +
                           Join("overtake", "Bye bye!", "Zu einfach!", "Der Undercut hat funktioniert!", "Bis später!", "Rein da!", "Gutes Manöver, Kumpel.", "Danke, der Nächste!", "So macht man das.", "Der DRS-Zug ist vorbei.") +
                           Join("first_place", "P1! P1!", "Wir haben es geschafft, Jungs!", "Ja! Ja! Ja!", "Wir führen das Rennen!", "Rein da!", "Was für eine Fahrt!");
                case "it":
                    return Header("for the Grand Prix 2026 campaign - Italian (used while the game runs in Italian)") +
                           Join("random", "Box, box!", "Ricevuto.", "Mamma mia!", "Spingi adesso, spingi!", "DRS attivato.", "Piano B, piano B!", "Stiamo controllando.", "Le gomme sono finite, amico.", "Bandiere blu! Bandiere blu!", "Attento ai limiti della pista.", "Tutto al massimo.", "Sto perdendo potenza!", "Andiamo!", "Dai, dai!", "Digli di spostarsi!", "Mi serve aria pulita.", "Com'è il distacco?", "Risparmiare carburante? Sul serio?", "No, no, no, no!", "Qual è il piano?", "Fai attenzione là fuori.", "Così si guida, amico!", "Tutto sotto controllo.", "Che gomme monta?", "Settore uno viola!", "Spingi come un matto!", "Prova radio.") +
                           Join("nitro", "DRS aperto, spingi!", "Modalità sorpasso attiva!", "Scarica, scarica!", "Piena potenza adesso!", "Modalità qualifica, vai!") +
                           Join("out_of_nitros", "Niente più energia.", "La batteria è scarica.", "Risparmia l'energia.", "Alza il piede e lascia scorrere.", "Non ci è rimasto niente.") +
                           Join("crash", "Mi ha colpito!", "Ma dove va?!", "Che pilota pericoloso!", "Incredibile!", "Stai scherzando?!", "Ho dei danni!", "L'ala anteriore è andata!", "Mi ha spinto fuori!", "Non è stata colpa mia!", "Controllate la macchina, per favore.") +
                           Join("overtake", "Ciao ciao!", "Troppo facile!", "L'undercut ha funzionato!", "A dopo!", "Vai dentro!", "Bella manovra, amico.", "Grazie, il prossimo!", "Così si fa.", "Il trenino DRS è finito.") +
                           Join("first_place", "P1! P1!", "Ce l'abbiamo fatta, ragazzi!", "Sì! Sì! Sì!", "In testa alla gara!", "Vai dentro!", "Che gara!");
            }
            return null;
        }

        // ------------------------------------------------------------------ files

        private static Lines Parse(string path)
        {
            var f = new Lines { Path = path };
            foreach (string t in Types) f.ByType[t] = new List<string>();
            string section = "random";
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant().Replace(" ", "_");
                    if (!f.ByType.ContainsKey(section)) f.ByType[section] = new List<string>();
                    continue;
                }
                f.ByType[section].Add(line);
            }
            return f;
        }

        private static void WriteIfMissing(string path, string text)
        {
            if (text == null || File.Exists(path)) return;
            File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
            Debug.Log("[HCT15] taunts: wrote defaults to " + path);
        }

        /// <summary>
        /// The lines file for a campaign folder ("" = the default) in the current game language: taunts.&lt;lang&gt;.txt
        /// when it exists, else taunts.txt. Defaults are written when missing (F1: English + five translations).
        /// </summary>
        private static Lines For(string folder)
        {
            string lang = Loc.Lang;
            string key = (folder ?? "") + "|" + lang;
            Lines f;
            if (_files.TryGetValue(key, out f)) return f;
            try
            {
                ContentRegistry.EnsureLoaded();
                string dir = string.IsNullOrEmpty(folder) ? ContentRegistry.ContentDir : Path.Combine(Path.Combine(ContentRegistry.ContentDir, "campaigns"), folder);
                string path = Path.Combine(dir, "taunts.txt");
                if (Directory.Exists(dir))
                {
                    WriteIfMissing(path, folder == "f1_2026" ? F1Defaults() : ChileanDefaults());
                    if (folder == "f1_2026") foreach (string l in F1Languages) WriteIfMissing(Path.Combine(dir, "taunts." + l + ".txt"), F1Defaults(l));
                }
                string localised = Path.Combine(dir, "taunts." + lang + ".txt");
                if (File.Exists(localised)) path = localised;
                if (!File.Exists(path)) return null;
                f = Parse(path);
                if (f.ByType["random"].Count == 0) { Debug.LogWarning("[HCT15] taunts: " + path + " has no [random] lines"); }
                Debug.Log("[HCT15] taunts: " + f.Count + " lines from " + path + " (language '" + lang + "')");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] taunts: " + folder + " unreadable: " + ex.Message); f = null; }
            _files[key] = f;
            return f;
        }

        /// <summary>
        /// One car's own lines: Game\HCT15Content\cars\taunts.&lt;car id&gt;.txt (or taunts.&lt;car id&gt;.&lt;lang&gt;.txt).
        /// A car with its own file keeps that voice in every campaign - the taxi and the micro talk like the
        /// street whether they are racing in Chile or behind the F1 grid - while every other car follows the
        /// campaign it is running in. Missing file, null: the caller falls back to the campaign's lines.
        /// </summary>
        private static Lines ForCar(string carId)
        {
            if (string.IsNullOrEmpty(carId)) return null;
            string lang = Loc.Lang;
            string key = "car:" + carId + "|" + lang;
            Lines f;
            if (_files.TryGetValue(key, out f)) return f;
            try
            {
                ContentRegistry.EnsureLoaded();
                string dir = Path.Combine(ContentRegistry.ContentDir ?? "", "cars");
                if (Directory.Exists(dir))
                {
                    string defaults = CarDefaults(carId);
                    if (defaults != null) WriteIfMissing(Path.Combine(dir, "taunts." + carId + ".txt"), defaults);
                }
                string path = Path.Combine(dir, "taunts." + carId + "." + lang + ".txt");
                if (!File.Exists(path)) path = Path.Combine(dir, "taunts." + carId + ".txt");
                if (!File.Exists(path)) { _files[key] = null; return null; }
                f = Parse(path);
                Debug.Log("[HCT15] taunts: " + f.Count + " lines of its own for '" + carId + "' from " + path);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] taunts: car file for '" + carId + "' unreadable: " + ex.Message); f = null; }
            _files[key] = f;
            return f;
        }

        // ------------------------------------------------------------------ registration

        public static void EnsureLoaded() { For(""); }

        // 2.0.11: which line of which section each speech key was given, for the spoken version (Voices.cs)
        private class ClipRef { public string Car; public string Section; public int Index; public Lines File; }
        private static readonly Dictionary<string, ClipRef> _clipOf = new Dictionary<string, ClipRef>();
        private static readonly Dictionary<string, List<int>> _cycle = new Dictionary<string, List<int>>();   // "car|section" -> lines not yet shown this round
        private static readonly System.Random _cycleRng = new System.Random();
        private static int _localLogged, _skipLogged;

        /// <summary>A line that names the road to Illapel (Illapel, the Choapa valley).</summary>
        private static bool NamesThePlace(string s)
        {
            return s != null && (s.IndexOf("Illapel", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("Choapa", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Where a line naming Illapel makes sense: the Rutas de Chile campaign (the road there), or a race in
        /// Illapel itself (the Plaza de Illapel, whose andas talk about their town).</summary>
        private static bool PlaceFits()
        {
            ModCampaign c = ModCampaigns.Active;
            if (c != null && string.Equals(c.Folder, "rutas_chile", StringComparison.OrdinalIgnoreCase)) return true;
            string race = StaticData.Race != null ? StaticData.Race.ItemId : null;
            return race != null && race.IndexOf("illapel", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 2.0.11 (2026-10-07: many more taunts, and every one of them heard): the game has 15
        /// speech slots per car (random 1-5, nitro 1-2, out_of_nitros, crash 1-3, overtake 1-3, first_place), each tied
        /// to one line, so 15 of a car's lines ever showed. Now, as a bubble is about to show (CarUIViewModel.ShowMessage
        /// prefix), its key is given the NEXT line of its section: every line of the section in a shuffled order, each
        /// once before any repeats, a new order every time the section is used up. The spoken clip follows (ClipOf).
        /// </summary>
        public static bool NextLine(string key)
        {
            try
            {
                ClipRef r;
                if (key == null || !_clipOf.TryGetValue(key, out r) || r.File == null) return false;
                List<string> pool;
                if (!r.File.ByType.TryGetValue(r.Section, out pool) || pool == null || pool.Count < 2) return false;
                string ck = r.Car + "|" + r.Section;
                List<int> left;
                if (!_cycle.TryGetValue(ck, out left) || left.Count == 0)
                {
                    left = new List<int>();
                    for (int i = 0; i < pool.Count; i++) left.Add(i);
                    for (int i = left.Count - 1; i > 0; i--) { int j = _cycleRng.Next(i + 1); int tmp = left[i]; left[i] = left[j]; left[j] = tmp; }
                    if (left.Count > 1 && left[left.Count - 1] == r.Index) { int tmp = left[0]; left[0] = left[left.Count - 1]; left[left.Count - 1] = tmp; }   // not the one just shown
                    _cycle[ck] = left;
                }
                int idx = left[left.Count - 1];
                left.RemoveAt(left.Count - 1);
                // 2.0.16 - on the stage that ends in Illapel, the carreta uses the taunts that mention Illapel
                // more: on a stage whose id says illapel, half the time a line of this section that names the place (not the
                // one just shown) - the shuffled cycle goes on underneath, so every other line still gets its turn
                try
                {
                    string race = StaticData.Race != null ? StaticData.Race.ItemId : null;
                    if (race != null && race.IndexOf("illapel", StringComparison.OrdinalIgnoreCase) >= 0 && _cycleRng.NextDouble() < 0.5)
                    {
                        var local = new List<int>();
                        for (int i = 0; i < pool.Count; i++)
                            if (i != r.Index && NamesThePlace(pool[i])) local.Add(i);
                        if (local.Count > 0) { left.Add(idx); idx = local[_cycleRng.Next(local.Count)]; left.Remove(idx); if (_localLogged++ < 6) Debug.Log("[HCT15] taunts: on '" + race + "' the line naming the place: " + key + " -> \"" + pool[idx] + "\""); }
                    }
                    // 2.1.5 - the lines that mention Illapel appear ONLY in the Rutas de Chile campaign: they are
                    // about the road to Illapel, out of that campaign they have no context; anywhere else such a line is skipped
                    // (it stays in the cycle, so it comes back when the campaign is Rutas de Chile); a race in Illapel itself
                    // keeps them too - the Plaza de Illapel is in the Chile campaign and its andas talk about their town
                    if (NamesThePlace(pool[idx]) && !PlaceFits())
                    {
                        int other = -1;
                        for (int j = left.Count - 1; j >= 0 && other < 0; j--) if (!NamesThePlace(pool[left[j]])) other = j;
                        if (other < 0) { left.Clear(); for (int i = 0; i < pool.Count; i++) if (i != idx && !NamesThePlace(pool[i])) left.Add(i); other = left.Count > 0 ? _cycleRng.Next(left.Count) : -1; }
                        if (other >= 0) { int keep = idx; idx = left[other]; left.RemoveAt(other); left.Insert(0, keep); if (_skipLogged++ < 3) Debug.Log("[HCT15] taunts: \"" + pool[keep] + "\" skipped - it names Illapel, and this is neither Rutas de Chile nor a race in Illapel"); }
                    }
                }
                catch (Exception) { }
                r.Index = idx;
                _override[key] = pool[idx];
                ContentRegistry.Register(key, pool[idx]);
                return true;
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] taunts: next line: " + ex.Message); return false; }
        }

        /// <summary>The car, section and line number (0-based, within the section) a speech key shows.</summary>
        public static bool ClipOf(string key, out string carId, out string section, out int index)
        {
            ClipRef r;
            carId = null; section = null; index = -1;
            if (key == null || !_clipOf.TryGetValue(key, out r)) return false;
            carId = r.Car; section = r.Section; index = r.Index;
            return true;
        }

        /// <summary>Registers the speech keys of one car from a lines file (deterministic per car).</summary>
        public static void RegisterFor(string carId, string folder)
        {
            Lines f = ForCar(carId) ?? ForCar(StreetCars.TauntsOf(carId)) ?? For(folder) ?? For("");   // a livery variant speaks with its base car's file
            if (f == null || string.IsNullOrEmpty(carId)) return;
            int seed = 17; foreach (char c in carId) seed = unchecked(seed * 31 + c);
            var rng = new System.Random(seed);
            for (int i = 0; i < Types.Length; i++)
            {
                List<string> pool;
                string section = Types[i];
                if (!f.ByType.TryGetValue(Types[i], out pool) || pool.Count == 0) { pool = f.ByType["random"]; section = "random"; }
                if (pool == null || pool.Count == 0) continue;
                if (Slots[i] == 0)
                {
                    string k = "speech_" + carId + "_" + Types[i];
                    int one = rng.Next(pool.Count);
                    ContentRegistry.Register(k, pool[one]); _override[k] = pool[one];
                    _clipOf[k] = new ClipRef { Car = carId, Section = section, Index = one, File = f };
                    continue;
                }
                var used = new HashSet<int>();
                for (int n = 1; n <= Slots[i]; n++)
                {
                    int idx = rng.Next(pool.Count); int guard = 0;
                    while (used.Contains(idx) && guard++ < 10 && used.Count < pool.Count) idx = rng.Next(pool.Count);
                    used.Add(idx);
                    string k = "speech_" + carId + "_" + Types[i] + n;
                    ContentRegistry.Register(k, pool[idx]); _override[k] = pool[idx];
                    _clipOf[k] = new ClipRef { Car = carId, Section = section, Index = idx, File = f };
                }
            }
        }

        /// <summary>Kept for callers that only know a car id: the F1 grid's file.</summary>
        public static void RegisterFor(string carId) { RegisterFor(carId, "f1_2026"); }

        /// <summary>Called before the game's own lookup: while a campaign's lines cover every car, every speech key is ours.</summary>
        public static bool TryOverride(string key, out string text)
        {
            text = null;
            if (!_all || key == null || !key.StartsWith("speech_")) return false;
            if (!_override.TryGetValue(key, out text)) return false;
            text = Wrap(text);
            return true;
        }

        /// <summary>
        /// A long line on two lines. The bubble is drawn for the retail lines (about 22 characters); the micro's
        /// "That car's a toy, mate!" (in Spanish) ran off the screen (2026-09-24). Broken at the space nearest the
        /// middle once the line passes WrapAt characters; an author's own line break is kept as it is.
        /// </summary>
        public const int WrapAt = 24;

        private static float[] _shippedChances;
        private static float _shippedInterval;

        /// <summary>
        /// hct15.settings.json "SpeechChance": the game's own chances of a speech bubble (SpeechController: random
        /// 4% every 10 s, overtake 8%, crash 10%, nitro 30%, first place 30%, out of nitros 100%) multiplied, once
        /// per race start. The shipped values are remembered so the factor can be changed between races.
        /// </summary>
        public static void ApplyChance()
        {
            try
            {
                SpeechController sc = SpeechController.Instance;
                if (sc == null) return;
                if (_shippedChances == null) _shippedChances = new[] { sc.m_randomChance, sc.m_overtakeChance, sc.m_crashChance, sc.m_nitroChance, sc.m_firstPlaceChance, sc.m_outOfNitroChance };
                float f = Mathf.Clamp(Settings.Data.SpeechChance, 0f, 25f);
                sc.m_randomChance = Mathf.Min(1f, _shippedChances[0] * f);
                sc.m_overtakeChance = Mathf.Min(1f, _shippedChances[1] * f);
                sc.m_crashChance = Mathf.Min(1f, _shippedChances[2] * f);
                sc.m_nitroChance = Mathf.Min(1f, _shippedChances[3] * f);
                sc.m_firstPlaceChance = Mathf.Min(1f, _shippedChances[4] * f);
                sc.m_outOfNitroChance = Mathf.Min(1f, _shippedChances[5] * f);
                // and the random bubbles' clock runs faster too (the game asks every 10 s; never under its 4 s between bubbles)
                if (_shippedInterval <= 0f) _shippedInterval = sc.SecondsBetweenRandomSpeeches;
                sc.SecondsBetweenRandomSpeeches = f > 1f ? Mathf.Max(sc.MinDelayBetweenSpeeches, _shippedInterval / f) : _shippedInterval;
                if (Mathf.Abs(f - 1f) > 0.01f)
                    Debug.Log("[HCT15] speech chance x" + f.ToString("0.0") + ": random " + sc.m_randomChance.ToString("0.00") + ", overtake " + sc.m_overtakeChance.ToString("0.00") + ", crash " + sc.m_crashChance.ToString("0.00"));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] speech chance: " + ex.Message); }
        }

        public static string Wrap(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= WrapAt || s.IndexOf('\n') >= 0) return s;
            int mid = s.Length / 2, best = -1;
            for (int i = 0; i < s.Length; i++)
                if (s[i] == ' ' && (best < 0 || Math.Abs(i - mid) < Math.Abs(best - mid))) best = i;
            return best <= 0 ? s : s.Substring(0, best) + "\n" + s.Substring(best + 1);
        }

        /// <summary>
        /// Mod cars always get their campaign's lines; every car while a campaign with a taunts.txt runs (or setting "all").
        /// Runs at start-up, when a campaign starts and whenever the game language changes (the files are re-read then).
        /// </summary>
        public static void Refresh()
        {
            try
            {
                _files.Clear();
                _override.Clear();
                string mode = (Settings.Data.ChileanTaunts ?? "chile").ToLowerInvariant();
                For("");   // the default file exists for editing even when no campaign is running
                var active = ModCampaigns.Active;
                string folder = active != null && For(active.Folder) != null ? active.Folder : "";
                _all = mode != "off" && (mode == "all" || (active != null && !active.Formula1 && For(active.Folder) != null));
                _allFrom = folder;
                int n = 0;
                if (F1Campaign.Available) foreach (var c in F1Campaign.GridCars) { RegisterFor(c.Name, "f1_2026"); n++; }
                foreach (string id in StreetCars.Ids) { RegisterFor(id, folder); n++; }   // their own cars\taunts.<id>.txt wins inside
                if (_all)
                {
                    var main = CarModelDataList.Instance;
                    if (main != null) foreach (var c in main.CarModelsExceptIgnored) { RegisterFor(c.Name, folder); n++; }
                }
                Debug.Log("[HCT15] taunts: lines for " + n + " car(s) in language '" + Loc.Lang + "'" + (_all ? " (all cars, from " + (folder.Length > 0 ? "campaigns\\" + folder : "the default file") + ")" : " (mod cars only)"));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] taunts refresh failed: " + ex.Message); }
        }
    }
}
