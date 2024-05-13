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
   "Plündern",          // 12
   "Kaufen",            // 13
   "Untersuchen",       // 14
   "Handeln",           // 15
   "Aktivieren",        // 16
   "Gilden Einladung",  // 17
};

const char* DE_WINDOW_TITLES[] =
{
   "Willkommen",        // 0
   "Zaubersprüche",     // 1
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
   "Klick mich, um deine Stimmung in glücklich zu ändern.", // 0
   "Klick mich, um deine Stimmung in neutral zu ändern.",   // 1
   "Klick mich, um deine Stimmung in traurig zu ändern.",   // 2
   "Klick mich, um deine Stimmung in wütend zu ändern.",    // 3
};

const char* DE_TOOLTIPS_ONLINEPLAYER[] =
{
   "Ein gesetztestreuer Spieler.",                                // 0
   "Ein Mörder! Sei vorsichtig, wenn du ihn triffst.",            // 1
   "Ein Gesetzloser, der gefährlich sein könnte.",                // 2
   "Ein Administrator des Servers.",                              // 3
   "Ein Guide, Barde oder sonstiges Mitglied des Admin-Teams.",   // 4
   "Ein Moderator."                                               // 5
};

const char* DE_TOOLTIPS_STATUSBAR[] =
{
   "Frames pro Sekunde - Ein hoher Wert ist besser (flüssiges Spielerlebnis).",              // 0
   "Deine Verbindungsverzögerung (Lag) zum Server. Je niedriger, umso besser.",              // 1
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
   "Charakternamen müssen zwischen 3 und 30 Zeichen lang sein.",                                                                                                                                                                                       // 3
   "Ungültiges Symbol im Namen verwendet.",                                                                                                                                                                                                            // 4
   "Dieser Name ist bereits vergeben.",                                                                                                                                                                                                                // 5
   "Namen von Monstern aus Meridian 59 sind nicht erlaubt." ,                                                                                                                                                                                          // 6
   "Namen von NPCs aus Meridian 59 sind nicht erlaubt.",                                                                                                                                                                                               // 7
   "Charaktere dürfen nicht nach bestehenden Gilden benannt werden.",                                                                                                                                                                                  // 8
   "Beleidigende oder anstößige Sprache ist in Charakternamen nicht erlaubt.",                                                                                                                                                                         // 9
   "Bitte wähle einen anderen Namen - dieser könnte im Spiel für Verwirrung sorgen.",                                                                                                                                                                  // 10
   "Bitte wähle einen anderen Namen - dieser gehört einem ehemaligen Entwickler oder Mitglied des Meridian 59 Teams und ist für sie für die Zukunft reserviert.",                                                                                      // 11
   "Charakterbeschreibungen dürfen nicht mehr als 1000 Zeichen enthalten.",                                                                                                                                                                            // 12
   "Du musst ein Geschlecht für deinen Charakter wählen."                                                                                                                                                                                              // 13
};

const char* DE_CHARSSELECTABILITYERROR[] =
{
   "Kein Fehler.",                                                                                                                                 // 0
   "Du hast keine weiteren Fähigkeitspunkte übrig, die du noch vergeben könntest.",                                                                // 1
   "Du hast bereits einen Zauber der Schule von Shal'ille gewählt, die in direktem Gegensatz zu Qor steht.",                                       // 2
   "Du hast bereits einen Zauber der Schule von Qor gewählt, die in direktem Gegensatz zu Shal'ille steht.",                                       // 3
   "Du musst mindestens zwei Fähigkeiten des ersten Ranges einer Schule ausgewählt haben, um Fähigkeiten des zweiten Ranges auswählen zu können."  // 4
};

const char* DE_NPCQUESTUI[] =
{
   "Beschreibung:",                                // 0
   "Voraussetzungen:",                             // 1
   "Anleitung:",                                   // 2
   "Akzeptieren",                                  // 3
   "Fortsetzen",                                   // 4
   "Schließen",                                    // 5
   "Quest - Hilfe",                                // 6
   "Klicke in der Übersicht auf eine Quest, um dessen Beschreibung anzuzeigen.\n\nWenn du alle Anforderungen erfüllst um eine Quest zu starten, "
   "wird diese in der Übersicht mit Gelb markiert. Aktuell laufende und aktive Quests werden in Grün angezeigt, vorausgesetzt der jeweilige NPC ist "
   "das richtige Ziel. Wird jedoch eine Quest in Weiß angezeigt, erfüllst du nicht alle Voraussetzungen.\n\nDie Voraussetzungen werden unter der "
   "Beschreibung der jeweiligen Quest angezeigt. Wobei die erfüllten Anforderungen in Grün und die nicht erfüllten in Rot angezeigt werden.\n\nKlicke auf "
   "'Akzeptieren' oder 'Fortsetzen' um eine neue Quest zu starten oder eine aktive Quest fortzusetzen. Wenn du eine Quest abschließen solltest, bei der ein NPC "
   "einen Gegenstand benötigt, wird dieser mit einem Klick auf die Buttons automatisch übergeben. Wenn du im Besitz von mehreren Exemplaren dieses Gegenstandes "
   "sein solltest, wird immer der letzte aus deinem Inventar abgegeben.\n\nTipp: Behalte immer deinen Quest-Log im Auge, um weitere Informationen rund um aktiven "
   "oder abgeschlossenen Quests zu erhalten.\n\nDrücke die rechte Maustaste in den Textfeldern mit den Beschreibungen oder Voraussetzungen. Damit gelangst du in "
   "den 'Kopier-Modus' und hast die Möglichkeit, diese zu Kopieren."    // 7
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
   "Gegenstände", // 3
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
	"Nome de usuário",    // 0
	"Senha",              // 1
	"Conectar",           // 2
	"Opções",             // 3
	"Ligado",             // 4
	"Desligado",          // 5
	"Chave",              // 6
};

const char* PT_WINDOW_TITLES[] =
{
	"Bem-vindo",     // 0
	"Feitiços",      // 1
	"Habilidades",   // 2
	"Ações",         // 3
	"Inventário",    // 4
	"Quantidade",    // 5
	"Negociar",      // 6
	"Opções",        // 7
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
	"Este é um jogador legal.",                                                                 // 0
	"Este jogador assassinou alguém. Tenha cuidado ao encontrá-lo!",                            // 1
	"Este jogador é um fora-da-lei e pode ser perigoso!",                                       // 2
	"Este jogador é um administrador do servidor.",                                             // 3
	"Este jogador é um guia/bardo ou outro tipo de membro da equipe.",                           // 4
	"Este jogador é um moderador."                                                              // 5
};

const char* PT_TOOLTIPS_STATUSBAR[] =
{
	"Quadros por segundo - Maior é melhor (jogabilidade mais fluida).",                 // 0
	"Sua velocidade de conexão com o servidor. Um valor menor significa conexão mais rápida.",   // 1
	"O número de jogadores online. Clique em mim para mostrar a lista de jogadores online.",     // 2
	"Mostra o humor atual do seu personagem.",                                          // 3
	"Se você desativar sua segurança, poderá atacar jogadores inocentes.",               // 4
	"Aqui você pode ver o horário atual do Meridian 59.",                               // 5
	"Isto mostra o nome atual da sala/área em que você está atualmente."                 // 6
};

const char* PT_DESCRIPTIONS_STATUSBAR[] =
{
	"FPS",                // 0
	"Ping",               // 1
	"Jogadores",          // 2
	"Humor",              // 3
	"Segurança",          // 4
	"Horário do Meridian",// 5
	"Sala/Área"           // 6
};

const char* PT_CHARINFONOTOKERROR_OKDIALOG[] =
{
	"Sem erro.", // 0
	"A criação de personagem falhou devido a um erro não especificado, por favor, tente novamente e entre em contato com um administrador in-game ou no Discord do Meridian 59 Server 104 se não conseguir criar um personagem.", // 1
	"Slot de personagem já em uso! Por favor, tente um slot de personagem diferente e entre em contato com um administrador in-game ou no Discord do Meridian 59 Server 104.", // 2
	"Os nomes de personagens devem ter entre 3 e 30 caracteres de comprimento, ou seu nome já está em uso.", // 3
	"Caractere inválido usado no nome.", // 4
	"O nome do seu personagem já foi escolhido por outra pessoa.", // 5
	"Você não pode escolher o nome de um monstro do Meridian 59.", // 6
	"Você não pode escolher o nome de um NPC do Meridian 59.", // 7
	"Você não pode nomear seu personagem com o nome de uma Guilda existente do Meridian 59.", // 8
	"Você não pode usar linguagem ofensiva no nome do seu personagem.", // 9
	"Por favor, escolha outro nome - este pode causar confusão no jogo.", // 10
	"Por favor, escolha outro nome, este pertence a um ex-desenvolvedor ou membro da equipe do Meridian 59 e está reservado para uso futuro.", // 11
	"As descrições de jogadores não podem ter mais de 1000 caracteres.", // 12
	"Você deve selecionar masculino ou feminino ao criar seu personagem." // 13
};

const char* PT_CHARSSELECTABILITYERROR[] =
{
	"Sem erro.", // 0
	"Você não tem mais pontos de habilidade para gastar!", // 1
	"Você já selecionou um feitiço de Shal'ille, uma escola diametralmente oposta a Qor.", // 2
	"Você já selecionou um feitiço de Qor, uma escola diametralmente oposta a Shal'ille.", // 3
	"Você precisa selecionar pelo menos dois habilidades de primeiro nível em uma escola para desbloquear o segundo nível dela." // 4
};

const char* PT_NPCQUESTUI[] =
{
	"Descrição:", // 0
	"Requisitos:", // 1
	"Instruções:", // 2
	"Aceitar", // 3
	"Continuar", // 4
	"Fechar", // 5
	"Ajuda - Missões", // 6
	"Clique em uma missão na lista de missões para ver sua descrição.\n\nSe você atender aos requisitos para iniciar uma missão, ela será mostrada em amarelo na lista de missões. As missões atualmente ativas são mostradas em verde se este NPC for o destino da missão. As missões são mostradas em branco se você não atender a todos os requisitos.\n\nOs requisitos são mostrados sob a descrição no lado direito da interface do usuário, com os requisitos atendidos mostrados em verde e os não atendidos em vermelho.\n\nClique em 'Aceitar'/'Continuar' para iniciar uma nova missão ou progredir em uma existente. Completar uma missão onde o NPC requer um item dará esse item ao NPC. Se você tiver várias cópias de um item, a última em seu inventário será dada.\n\nVerifique seu próprio Registro de Missões para obter informações sobre missões atuais e concluídas.\n\nClique com o botão direito do mouse nas caixas de texto Descrição e Requisitos para alternar para o modo 'cópia' onde o texto pode ser copiado da caixa." // 7
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
	"Missão"       // 7
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
