#include "stdafx.h"

/**************************************************************************************/
/***************************      ENGLISH     *****************************************/
/**************************************************************************************/

const char* EN[] =
{
   "Username",    // 0
   "Password",    // 1
   "Connect",     // 2
   "Options",     // 3
   "On",          // 4
   "Off",         // 5
   "Key",         // 6
};

const char* EN_WINDOW_TITLES[] =
{
   "Welcome",     // 0
   "Spells",      // 1
   "Skills",      // 2
   "Actions",     // 3
   "Inventory",   // 4
   "Amount",      // 5
   "Trade",       // 6
   "Options",     // 7
   "Attributes",  // 8
   "Mail",        // 9
   "Guild",       // 10
   "Players",     // 11
};

const char* EN_TOOLTIPS_MOOD[] =
{
   "Click me to change your mood to happy.",    // 0
   "Click me to change your mood to neutral.",  // 1
   "Click me to change your mood to sad.",      // 2
   "Click me to change your mood to angry.",    // 3
};

const char* EN_TOOLTIPS_ONLINEPLAYER[] =
{
   "This is a lawful player.",                                             // 0
   "This player has murdered someone. Be careful when you meet him!",      // 1
   "This player is an outlaw and may be dangerous!",                       // 2
   "This player is an admin of the server.",                               // 3
   "This player is a guide/barde or other kind of member of the staff.",   // 4
   "This player is a moderator."                                           // 5
};

const char* EN_TOOLTIPS_STATUSBAR[] =
{
   "Frames per second - Higher is better (more fluid Gameplay).",                // 0
   "Your Connectionspeed to the Server. Lower value means faster connection.",   // 1
   "The number of players online. Click me to show the list of online Players.", // 2
   "Shows the actual mood of your character.",                                   // 3
   "If you change your safety to Off you can attack inocent players.",           // 4
   "Here you can see the actual Meridian 59 time.",                              // 5
   "This shows you the actual room / area name, in which you actually are."      // 6
};

const char* EN_DESCRIPTIONS_STATUSBAR[] =
{
   "FPS",               // 0
   "Ping",              // 1
   "Players",           // 2
   "Mood",              // 3
   "Safety",            // 4
   "Meridian-Time",     // 5
   "Room / Area"        // 6
};

const char* EN_CHARINFONOTOKERROR_OKDIALOG[] =
{
   "No error.",																																										     // 0
   "Character creation failed due to an unspecified error, please try again and contact an admin in-game or at Meridian 59 Server 104 Discord if you are unable to create a character.", // 1
   "Character slot already in use! Please try a different character slot, and contact an admin in-game or at Meridian 59 Server 104 Discord.",                                           // 2
   "Character names must be between 3 and 30 characters long, or your name is already in use.",                                                                                          // 3
   "Invalid character used in name.",																																				     // 4
   "Your character name is already taken by someone else.",																																 // 5
   "You may not pick the name of a Meridian 59 monster.",																																 // 6
   "You may not pick the name of a Meridian 59 NPC.",																																	 // 7
   "You may not name your character after an existing Meridian 59 Guild.",																												 // 8
   "You may not use offensive language in your character name.",																														 // 9
   "Please pick another name - this one could cause confusion in-game.",																											     // 10
   "Please pick another name, this one belongs to a former Meridian 59 developer or staff member and is reserved for their future use.",												 // 11
   "Player descriptions cannot be more than 1000 characters.",																															 // 12
   "You must select either male or female when creating your character."																												 // 13
};
const char* EN_CHARSSELECTABILITYERROR[] =
{
   "No error.",                                                                                                // 0
   "You don't have any ability points left to spend!",                                                         // 1
   "You have already selected a Shal'ille spell, a school diametrically opposed to Qor.",                      // 2
   "You have already selected a Qor spell, a school diametrically opposed to Shal'ille.",                      // 3
   "You have to select at least two first level abilities in a school to unlock its second level."             // 4
};

const char* EN_NPCQUESTUI[] =
{
   "Description:",                                                                                             // 0
   "Requirements:",                                                                                            // 1
   "Instructions:",                                                                                            // 2
   "Accept",                                                                                                   // 3
   "Continue",                                                                                                 // 4
   "Close",                                                                                                    // 5
   "Help - Quests",                                                                                            // 6
   "Click on a quest in the quest list to view its description.\n\nIf you meet the requirements to start a quest, it will be shown in "
   "yellow in the quest list. Currently active Quests are shown in green if this NPC is the destination for the quest. Quests are shown "
   "in white if you do not meet all the requirements.\n\nRequirements are shown under the description on the right-hand side of the UI, "
   "with met requirements shown in green and unmet ones in red.\n\nClick 'Accept'/'Continue' to start a new quest or progress an existing one. "
   "Completing a Quest where the NPC requires an item will give that item to the NPC. If you have multiple copies of an item, the last one in your "
   "inventory will be given.\n\nCheck your own Quest Log for information on current and completed quests.\n\nRight-click the Description and "
   "Requirements text boxes to switch to 'copy' mode where text can be copied from the box. "                  // 7
};

const char* EN_MISC[] =
{
   " (in use)"  // 0
};

const char* EN_TOOLTIPS_TARGET[] =
{
   "Inspect",    // 0
   "Attack",     // 1
   "Activate",   // 2
   "Items",      // 3
   "Buy",        // 4
   "Trade",      // 5
   "Loot",       // 6
   "Quest"       // 7
};

/**************************************************************************************/
/***************************      GERMAN      *****************************************/
/**************************************************************************************/

const char* DE[] =
{
   "Benutzername",      // 0
   "Passwort",          // 1
   "Verbinden",         // 2
   "Optionen",          // 3
   "Ein",               // 4
   "Aus",               // 5
   "Taste",             // 6
   "Angriff",           // 7
   "Rasten",            // 8
   "Tanzen",            // 9
   "Winken",            // 10
   "Zeigen",            // 11
   "Pl�ndern",          // 12
   "Kaufen",            // 13
   "Untersuchen",       // 14
   "Handeln",           // 15
   "Aktivieren",        // 16
   "Gilden Einladung",  // 17
};

const char* DE_WINDOW_TITLES[] =
{
   "Willkommen",        // 0
   "Zauberspr�che",     // 1
   "Fertigkeiten",      // 2
   "Aktionen",          // 3
   "Inventar",          // 4
   "Menge",             // 5
   "Handel",            // 6
   "Optionen",          // 7
   "Attribute",         // 8
   "Post",              // 9
   "Gilde",             // 10
   "Spieler",           // 11
};

const char* DE_TOOLTIPS_MOOD[] =
{
   "Klick mich, um deine Stimmung in gl�cklich zu �ndern.", // 0
   "Klick mich, um deine Stimmung in neutral zu �ndern.",   // 1
   "Klick mich, um deine Stimmung in traurig zu �ndern.",   // 2
   "Klick mich, um deine Stimmung in w�tend zu �ndern.",    // 3
};

const char* DE_TOOLTIPS_ONLINEPLAYER[] =
{
   "Ein gesetztestreuer Spieler.",                                // 0
   "Ein M�rder! Sei vorsichtig, wenn du ihn triffst.",            // 1
   "Ein Gesetzloser, der gef�hrlich sein k�nnte.",                // 2
   "Ein Administrator des Servers.",                              // 3
   "Ein Guide, Barde oder sonstiges Mitglied des Admin-Teams.",   // 4
   "Ein Moderator."                                               // 5
};

const char* DE_TOOLTIPS_STATUSBAR[] =
{
   "Frames pro Sekunde - Ein hoher Wert ist besser (fl�ssiges Spielerlebnis).",              // 0
   "Deine Verbindungsverz�gerung (Lag) zum Server. Je niedriger, umso besser.",              // 1
   "Anzahl der aktuell eingeloggten Spieler. Klick mich, um die Spielerliste anzuzeigen.",   // 2
   "Zeigt die aktuelle Stimmung Deines Characters an.",                                      // 3
   "Wenn Du Deine Sicherheit auf Aus stellst, kannst Du unschuldige Spieler angreifen",      // 4
   "Hier siehst Du die aktuelle Meridian 59 Zeit.",                                          // 5
   "Zeigt Dir den aktuellen Raum / Bereich an, indem Du Dich befindest."                     // 6
};

const char* DE_DESCRIPTIONS_STATUSBAR[] =
{
   "FPS",                  // 0
   "Ping",                 // 1
   "Spieler",              // 2
   "Stimmung",             // 3
   "Sicherheit",           // 4
   "Meridian-Zeit",        // 5
   "Raum / Gebiet"         // 6
};

const char* DE_CHARINFONOTOKERROR_OKDIALOG[] =
{
   "Kein Fehler.",                                                                                                                                                                                                                                     // 0
   "Charaktererstellung aufgrund eines unspezifizierten Fehlers fehlgeschlagen, bitte versuche es erneut und kontaktiere einen Administrator im Spiel oder auf Meridian 59 Server 104 Discord wenn Du nicht in der Lage bist, einen Charakter zu erstellen.", // 1
   "Charakterplatz bereits belegt! Bitte versuche einen anderen Charakterplatz und kontaktiere einen Administrator im Spiel oder auf Meridian 59 Server 104 Discord.",                                                                                        // 2
   "Charakternamen m�ssen zwischen 3 und 30 Zeichen lang sein.",                                                                                                                                                                                       // 3
   "Ung�ltiges Symbol im Namen verwendet.",                                                                                                                                                                                                            // 4
   "Dieser Name ist bereits vergeben.",                                                                                                                                                                                                                // 5
   "Namen von Monstern aus Meridian 59 sind nicht erlaubt." ,                                                                                                                                                                                          // 6
   "Namen von NPCs aus Meridian 59 sind nicht erlaubt.",                                                                                                                                                                                               // 7
   "Charaktere d�rfen nicht nach bestehenden Gilden benannt werden.",                                                                                                                                                                                  // 8
   "Beleidigende oder anst��ige Sprache ist in Charakternamen nicht erlaubt.",                                                                                                                                                                         // 9
   "Bitte w�hle einen anderen Namen - dieser k�nnte im Spiel f�r Verwirrung sorgen.",                                                                                                                                                                  // 10
   "Bitte w�hle einen anderen Namen - dieser geh�rt einem ehemaligen Entwickler oder Mitglied des Meridian 59 Teams und ist f�r sie f�r die Zukunft reserviert.",                                                                                      // 11
   "Charakterbeschreibungen d�rfen nicht mehr als 1000 Zeichen enthalten.",                                                                                                                                                                            // 12
   "Du musst ein Geschlecht f�r deinen Charakter w�hlen."                                                                                                                                                                                              // 13
};

const char* DE_CHARSSELECTABILITYERROR[] =
{
   "Kein Fehler.",                                                                                                                                 // 0
   "Du hast keine weiteren F�higkeitspunkte �brig, die du noch vergeben k�nntest.",                                                                // 1
   "Du hast bereits einen Zauber der Schule von Shal'ille gew�hlt, die in direktem Gegensatz zu Qor steht.",                                       // 2
   "Du hast bereits einen Zauber der Schule von Qor gew�hlt, die in direktem Gegensatz zu Shal'ille steht.",                                       // 3
   "Du musst mindestens zwei F�higkeiten des ersten Ranges einer Schule ausgew�hlt haben, um F�higkeiten des zweiten Ranges ausw�hlen zu k�nnen."  // 4
};

const char* DE_NPCQUESTUI[] =
{
   "Beschreibung:",                                // 0
   "Voraussetzungen:",                             // 1
   "Anleitung:",                                   // 2
   "Akzeptieren",                                  // 3
   "Fortsetzen",                                   // 4
   "Schlie�en",                                    // 5
   "Quest - Hilfe",                                // 6
   "Klicke in der �bersicht auf eine Quest, um dessen Beschreibung anzuzeigen.\n\nWenn du alle Anforderungen erf�llst um eine Quest zu starten, "
   "wird diese in der �bersicht mit Gelb markiert. Aktuell laufende und aktive Quests werden in Gr�n angezeigt, vorausgesetzt der jeweilige NPC ist "
   "das richtige Ziel. Wird jedoch eine Quest in Wei� angezeigt, erf�llst du nicht alle Voraussetzungen.\n\nDie Voraussetzungen werden unter der "
   "Beschreibung der jeweiligen Quest angezeigt. Wobei die erf�llten Anforderungen in Gr�n und die nicht erf�llten in Rot angezeigt werden.\n\nKlicke auf "
   "'Akzeptieren' oder 'Fortsetzen' um eine neue Quest zu starten oder eine aktive Quest fortzusetzen. Wenn du eine Quest abschlie�en solltest, bei der ein NPC "
   "einen Gegenstand ben�tigt, wird dieser mit einem Klick auf die Buttons automatisch �bergeben. Wenn du im Besitz von mehreren Exemplaren dieses Gegenstandes "
   "sein solltest, wird immer der letzte aus deinem Inventar abgegeben.\n\nTipp: Behalte immer deinen Quest-Log im Auge, um weitere Informationen rund um aktiven "
   "oder abgeschlossenen Quests zu erhalten.\n\nDr�cke die rechte Maustaste in den Textfeldern mit den Beschreibungen oder Voraussetzungen. Damit gelangst du in "
   "den 'Kopier-Modus' und hast die M�glichkeit, diese zu Kopieren."    // 7
};

const char* DE_MISC[] =
{
   " (in Benutzung)"  // 0
};

const char* DE_TOOLTIPS_TARGET[] =
{
   "Inspizieren", // 0
   "Angreifen",   // 1
   "Aktivieren",  // 2
   "Gegenst�nde", // 3
   "Kaufen",      // 4
   "Anbieten",    // 5
   "Aufheben",    // 6
   "Auftrag"      // 7
};

/**************************************************************************************/
/***************************    Portuguese    *****************************************/
/**************************************************************************************/

const char* PT[] =
{
	"Nome de usu�rio",    // 0
	"Senha",              // 1
	"Conectar",           // 2
	"Op��es",             // 3
	"Ligado",             // 4
	"Desligado",          // 5
	"Chave",              // 6
};

const char* PT_WINDOW_TITLES[] =
{
	"Bem-vindo",     // 0
	"Feiti�os",      // 1
	"Habilidades",   // 2
	"A��es",         // 3
	"Invent�rio",    // 4
	"Quantidade",    // 5
	"Negociar",      // 6
	"Op��es",        // 7
	"Atributos",     // 8
	"Correio",       // 9
	"Guilda",        // 10
	"Jogadores",     // 11
};

const char* PT_TOOLTIPS_MOOD[] =
{
	"Clique em mim para mudar seu humor para feliz.",      // 0
	"Clique em mim para mudar seu humor para neutro.",    // 1
	"Clique em mim para mudar seu humor para triste.",    // 2
	"Clique em mim para mudar seu humor para irritado.",  // 3
};

const char* PT_TOOLTIPS_ONLINEPLAYER[] =
{
	"Este � um jogador legal.",                                                                 // 0
	"Este jogador assassinou algu�m. Tenha cuidado ao encontr�-lo!",                            // 1
	"Este jogador � um fora-da-lei e pode ser perigoso!",                                       // 2
	"Este jogador � um administrador do servidor.",                                             // 3
	"Este jogador � um guia/bardo ou outro tipo de membro da equipe.",                           // 4
	"Este jogador � um moderador."                                                              // 5
};

const char* PT_TOOLTIPS_STATUSBAR[] =
{
	"Quadros por segundo - Maior � melhor (jogabilidade mais fluida).",                 // 0
	"Sua velocidade de conex�o com o servidor. Um valor menor significa conex�o mais r�pida.",   // 1
	"O n�mero de jogadores online. Clique em mim para mostrar a lista de jogadores online.",     // 2
	"Mostra o humor atual do seu personagem.",                                          // 3
	"Se voc� desativar sua seguran�a, poder� atacar jogadores inocentes.",               // 4
	"Aqui voc� pode ver o hor�rio atual do Meridian 59.",                               // 5
	"Isto mostra o nome atual da sala/�rea em que voc� est� atualmente."                 // 6
};

const char* PT_DESCRIPTIONS_STATUSBAR[] =
{
	"FPS",                // 0
	"Ping",               // 1
	"Jogadores",          // 2
	"Humor",              // 3
	"Seguran�a",          // 4
	"Hor�rio do Meridian",// 5
	"Sala/�rea"           // 6
};

const char* PT_CHARINFONOTOKERROR_OKDIALOG[] =
{
	"Sem erro.", // 0
	"A cria��o de personagem falhou devido a um erro n�o especificado, por favor, tente novamente e entre em contato com um administrador in-game ou no Discord do Meridian 59 Server 104 se n�o conseguir criar um personagem.", // 1
	"Slot de personagem j� em uso! Por favor, tente um slot de personagem diferente e entre em contato com um administrador in-game ou no Discord do Meridian 59 Server 104.", // 2
	"Os nomes de personagens devem ter entre 3 e 30 caracteres de comprimento, ou seu nome j� est� em uso.", // 3
	"Caractere inv�lido usado no nome.", // 4
	"O nome do seu personagem j� foi escolhido por outra pessoa.", // 5
	"Voc� n�o pode escolher o nome de um monstro do Meridian 59.", // 6
	"Voc� n�o pode escolher o nome de um NPC do Meridian 59.", // 7
	"Voc� n�o pode nomear seu personagem com o nome de uma Guilda existente do Meridian 59.", // 8
	"Voc� n�o pode usar linguagem ofensiva no nome do seu personagem.", // 9
	"Por favor, escolha outro nome - este pode causar confus�o no jogo.", // 10
	"Por favor, escolha outro nome, este pertence a um ex-desenvolvedor ou membro da equipe do Meridian 59 e est� reservado para uso futuro.", // 11
	"As descri��es de jogadores n�o podem ter mais de 1000 caracteres.", // 12
	"Voc� deve selecionar masculino ou feminino ao criar seu personagem." // 13
};

const char* PT_CHARSSELECTABILITYERROR[] =
{
	"Sem erro.", // 0
	"Voc� n�o tem mais pontos de habilidade para gastar!", // 1
	"Voc� j� selecionou um feiti�o de Shal'ille, uma escola diametralmente oposta a Qor.", // 2
	"Voc� j� selecionou um feiti�o de Qor, uma escola diametralmente oposta a Shal'ille.", // 3
	"Voc� precisa selecionar pelo menos dois habilidades de primeiro n�vel em uma escola para desbloquear o segundo n�vel dela." // 4
};

const char* PT_NPCQUESTUI[] =
{
	"Descri��o:", // 0
	"Requisitos:", // 1
	"Instru��es:", // 2
	"Aceitar", // 3
	"Continuar", // 4
	"Fechar", // 5
	"Ajuda - Miss�es", // 6
	"Clique em uma miss�o na lista de miss�es para ver sua descri��o.\n\nSe voc� atender aos requisitos para iniciar uma miss�o, ela ser� mostrada em amarelo na lista de miss�es. As miss�es atualmente ativas s�o mostradas em verde se este NPC for o destino da miss�o. As miss�es s�o mostradas em branco se voc� n�o atender a todos os requisitos.\n\nOs requisitos s�o mostrados sob a descri��o no lado direito da interface do usu�rio, com os requisitos atendidos mostrados em verde e os n�o atendidos em vermelho.\n\nClique em 'Aceitar'/'Continuar' para iniciar uma nova miss�o ou progredir em uma existente. Completar uma miss�o onde o NPC requer um item dar� esse item ao NPC. Se voc� tiver v�rias c�pias de um item, a �ltima em seu invent�rio ser� dada.\n\nVerifique seu pr�prio Registro de Miss�es para obter informa��es sobre miss�es atuais e conclu�das.\n\nClique com o bot�o direito do mouse nas caixas de texto Descri��o e Requisitos para alternar para o modo 'c�pia' onde o texto pode ser copiado da caixa." // 7
};

const char* PT_MISC[] =
{
	" (em uso)"  // 0
};

const char* PT_TOOLTIPS_TARGET[] =
{
	"Inspecionar", // 0
	"Atacar",      // 1
	"Ativar",      // 2
	"Itens",       // 3
	"Comprar",     // 4
	"Negociar",    // 5
	"Saquear",     // 6
	"Miss�o"       // 7
};

/**************************************************************************************/
/**************************************************************************************/

const char* GetLangLabel(const LANGSTR::Enum ID)
{
   using ::Meridian59::Ogre::OgreClient;
   using ::Meridian59::Common::Enums::LanguageCode;

   switch (OgreClient::Singleton->Config->Language)
   {
   case LanguageCode::German: return DE[ID];
   case LanguageCode::Portuguese: return PT[ID];
   default:                   return EN[ID];
   }
};

const char* GetLangWindowTitle(const LANGSTR_WINDOW_TITLE::Enum ID)
{
   using ::Meridian59::Ogre::OgreClient;
   using ::Meridian59::Common::Enums::LanguageCode;

   switch (OgreClient::Singleton->Config->Language)
   {
   case LanguageCode::German: return DE_WINDOW_TITLES[ID];
   case LanguageCode::Portuguese: return PT_WINDOW_TITLES[ID];
   default:                   return EN_WINDOW_TITLES[ID];
   }
};

const char* GetLangTooltipMood(const LANGSTR_TOOLTIP_MOOD::Enum ID)
{
   using ::Meridian59::Ogre::OgreClient;
   using ::Meridian59::Common::Enums::LanguageCode;

   switch (OgreClient::Singleton->Config->Language)
   {
   case LanguageCode::German: return DE_TOOLTIPS_MOOD[ID];
   case LanguageCode::Portuguese: return PT_TOOLTIPS_MOOD[ID];
   default:                   return EN_TOOLTIPS_MOOD[ID];
   }
};

const char* GetLangTooltipOnlinePlayer(const LANGSTR_TOOLTIP_ONLINEPLAYER::Enum ID)
{
   using ::Meridian59::Ogre::OgreClient;
   using ::Meridian59::Common::Enums::LanguageCode;

   switch (OgreClient::Singleton->Config->Language)
   {
   case LanguageCode::German: return DE_TOOLTIPS_ONLINEPLAYER[ID];
   case LanguageCode::Portuguese: return PT_TOOLTIPS_ONLINEPLAYER[ID];
   default:                   return EN_TOOLTIPS_ONLINEPLAYER[ID];
   }
};

const char* GetLangTooltipStatusBar(const LANGSTR_TOOLTIP_STATUSBAR::Enum ID)
{
   using ::Meridian59::Ogre::OgreClient;
   using ::Meridian59::Common::Enums::LanguageCode;

   switch (OgreClient::Singleton->Config->Language)
   {
   case LanguageCode::German: return DE_TOOLTIPS_STATUSBAR[ID];
   case LanguageCode::Portuguese: return PT_TOOLTIPS_STATUSBAR[ID];
   default:                   return EN_TOOLTIPS_STATUSBAR[ID];
   }
};

const char* GetLangDescriptionStatusBar(const LANGSTR_DESCRIPTION_STATUSBAR::Enum ID)
{
   using ::Meridian59::Ogre::OgreClient;
   using ::Meridian59::Common::Enums::LanguageCode;

   switch (OgreClient::Singleton->Config->Language)
   {
   case LanguageCode::German: return DE_DESCRIPTIONS_STATUSBAR[ID];
   case LanguageCode::Portuguese: return PT_DESCRIPTIONS_STATUSBAR[ID];
   default:                   return EN_DESCRIPTIONS_STATUSBAR[ID];
   }
};

const char* GetCharInfoNotOKErrorOkDialog(const LANGSTR_CHARINFONOTOKERROR_OKDIALOG::Enum ID)
{
   using ::Meridian59::Ogre::OgreClient;
   using ::Meridian59::Common::Enums::LanguageCode;

   switch (OgreClient::Singleton->Config->Language)
   {
   case LanguageCode::German: return DE_CHARINFONOTOKERROR_OKDIALOG[3];
   case LanguageCode::Portuguese: return PT_CHARINFONOTOKERROR_OKDIALOG[ID];
   default:                   return EN_CHARINFONOTOKERROR_OKDIALOG[3];
   }
};

const char* GetLangCharSelectAbilityError(const LANGSTR_CHARSSELECTABILITYERROR::Enum ID)
{
   using ::Meridian59::Ogre::OgreClient;
   using ::Meridian59::Common::Enums::LanguageCode;

   switch (OgreClient::Singleton->Config->Language)
   {
   case LanguageCode::German: return DE_CHARSSELECTABILITYERROR[ID];
   case LanguageCode::Portuguese: return PT_CHARSSELECTABILITYERROR[ID];
   default:                   return EN_CHARSSELECTABILITYERROR[ID];
   }
};

const char* GetLangNPCQuestUI(const LANGSTR_NPCQUESTUI::Enum ID)
{
   using ::Meridian59::Ogre::OgreClient;
   using ::Meridian59::Common::Enums::LanguageCode;

   switch (OgreClient::Singleton->Config->Language)
   {
   case LanguageCode::German: return DE_NPCQUESTUI[ID];
   case LanguageCode::Portuguese: return PT_NPCQUESTUI[ID];
   default:                   return EN_NPCQUESTUI[ID];
   }
};

const char* GetLangMisc(const LANGSTR_MISC::Enum ID)
{
   using ::Meridian59::Ogre::OgreClient;
   using ::Meridian59::Common::Enums::LanguageCode;

   switch (OgreClient::Singleton->Config->Language)
   {
   case LanguageCode::German: return DE_MISC[ID];
   case LanguageCode::Portuguese: return PT_MISC[ID];
   default:                   return EN_MISC[ID];
   }
};

const char* GetLangTooltipTarget(const LANGSTR_TOOLTIP_TARGET::Enum ID)
{
   using ::Meridian59::Ogre::OgreClient;
   using ::Meridian59::Common::Enums::LanguageCode;

   switch (OgreClient::Singleton->Config->Language)
   {
   case LanguageCode::German: return DE_TOOLTIPS_TARGET[ID];
   case LanguageCode::Portuguese: return PT_TOOLTIPS_TARGET[ID];
   default:                   return EN_TOOLTIPS_TARGET[ID];
   }
};
