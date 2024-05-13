using System.Globalization;

namespace Meridian59.Patcher
{
    // Workaround to avoid shipping an extra folder + dll with the updater
    // to display German strings.

    // New languages could be added by:
    //   1. Add the name to the LanguageIdentifier enum.
    //   2. Assign it to languageIdentifier in SetLanguageIdentifier.
    //   3. Add string(s) to the const section.
    //   4. Add the case for your language string to the appropriate string getter.
    public class LanguageHandler
    {
        #region Constants
        private enum LanguageIdentifier
        {
            English,
            German,
            Portuguese
        }

        private const string ERROR_EN = "Error";
        private const string ERROR_DE = "Fehler";
        private const string ERROR_PT = "Erro";

        private const string ABORT_EN = "Abort";
        private const string ABORT_DE = "Abbrechen";
        private const string ABORT_PT = "Abortar";

        private const string INFO_EN = "Info";
        private const string INFO_DE = "Info";
        private const string INFO_PT = "Informação";

        private const string CONFIRMCANCEL_EN = "Are you sure you want to cancel the client update?";
        private const string CONFIRMCANCEL_DE = "Möchtest du das Update wirklich abbrechen?";
        private const string CONFIRMCANCEL_PT = "Tem certeza de que deseja cancelar a atualização do cliente?";

        private const string CLIENTEXEMISSINGERROR_EN = "Fatal error finding the client executable. Try to disable your " + 
            "anti-virus software and repeat patching. If this issue persists please reach out for support in Meridian 59 104 Discord #technical-support channel.";
        private const string CLIENTEXEMISSINGERROR_DE = "Schwerer Fehler beim Auffinden der Startdatei des Clients. Versuche deine " +
            "Anti-Viren Software zu deaktivieren und das Patchen zu wiederholen. Sollte dieser Fehler weiterhin bestehen, dann " +
            "discord meridian 59 server 104 #technical-support.";
        private const string CLIENTEXEMISSINGERROR_PT = "Erro fatal ao encontrar o executável do cliente. Tente desabilitar o seu " +
        "software antivírus e repita a aplicação do patch. Se este problema persistir, entre em contato com o suporte no canal #technical-support do Discord Meridian 59 104.";

        private const string URLINFOMISSING_EN = "Patch information from file {0} is missing. You may be " +
            "able to obtain this file automatically by using the game client to connect to a valid server. " +
            "If you are unable to do this, you will need to run the client installer again.";
        private const string URLINFOMISSING_DE = "Patch Information der Datei {0} fehlt. Du kannst diese Datei " +
            "möglicherweise automatisch abrufen, indem du den Client startest und dich mit einem gültigen " +
            "Server verbindest. Wenn dies nicht möglich ist, musst du das Installationsprogramm des Clients erneut ausführen ";
        private const string URLINFOMISSING_PT = "As informações de patch do arquivo {0} estão ausentes. Você pode " +
        "obter este arquivo automaticamente usando o cliente do jogo para se conectar a um servidor válido. " +
        "Se você não conseguir fazer isso, precisará executar novamente o instalador do cliente.";

        private const string URLINFOBROKEN_EN = "Patch information file {0} is corrupted. You may be " +
            "able to reobtain this file automatically by using the game client to connect to a valid server. " +
            "If you are unable to do this, you will need to run the client installer again.";
        private const string URLINFOBROKEN_DE = "Patch Information der Datei {0} ist beschädigt . Du kannst diese Datei " +
            "möglicherweise automatisch wiederherstellen, indem du den Client startest und dich mit einem gültigen " +
            "Server verbindest. Wenn dies nicht möglich ist, musst du das Installationsprogramm des Clients erneut ausführen ";
        private const string URLINFOBROKEN_PT = "O arquivo de informações de patch {0} está corrompido. Você pode " +
        "obter este arquivo novamente automaticamente usando o cliente do jogo para se conectar a um servidor válido. " +
        "Se você não conseguir fazer isso, precisará executar novamente o instalador do cliente.";

        private const string JSONDOWNLOADFAILED_EN = "Download of patch data file failed. This could be due to an " +
            "internet connection issue or patch server issues. If retrying fails, please check your connection " +
            "or try again later.";
        private const string JSONDOWNLOADFAILED_DE = "Der Download der Patch-Datei ist fehlgeschlagen. Dies kann an " +
            "einem Problem mit deiner Internetverbindung oder dem Patch-Server zusammenhängen. Wenn der Wiederholungsversuch " +
            "fehlschlägt, überprüfe deine Internetverbindung oder versuchen es später erneut.";
        private const string JSONDOWNLOADFAILED_PT = "O download do arquivo de dados do patch falhou. Isso pode ser devido a um " +
        "problema de conexão com a internet ou problemas no servidor de patch. Se tentar novamente não funcionar, por favor, verifique sua conexão " +
        "ou tente novamente mais tarde.";

        private const string RETRYINGFILE_EN = "Download of file {0} failed, retrying...\n";
        private const string RETRYINGFILE_DE = "Download der Datei {0} ist fehlgeschlagen, neuer Versuch...\n";
        private const string RETRYINGFILE_PT = "O download do arquivo {0} falhou, tentando novamente...\n";

        private const string FILEFAILED_EN = "Download of file {0} failed. This could be due to an " +
            "open client blocking the file from being patched, no internet connection or patch server " +
            "issues. Please try again later.";
        private const string FILEFAILED_DE = "Download der Datei {0} ist fehlgeschlagen. Dies kann daran liegen " +
            "das ein weiterer Client geöffnet ist und dadurch die Datei geblockt wird, es ein Problem mit deiner " +
            "Internetverbindung gibt oder einem Fehler mit dem Patch-Server. Bitte versuche es später erneut.";
        private const string FILEFAILED_PT = "O download do arquivo {0} falhou. Isso pode ser devido a um " +
        "cliente aberto bloqueando o arquivo de ser atualizado, falta de conexão com a internet ou problemas no servidor de patch. " +
        "Por favor, tente novamente mais tarde.";

        private const string PATCHDOWNLOADFAILED_EN = "Patch information download failed!\n";
        private const string PATCHDOWNLOADFAILED_DE = "Herunterladen der Patch-Informationen fehlgeschlagen!\n";
        private const string PATCHDOWNLOADFAILED_PT = "Falha no download das informações do patch!\n";

        private const string FILEDOWNLOADED_EN = "Downloaded file {0}\n...";
        private const string FILEDOWNLOADED_DE = "Datei {0} heruntergeladen\n...";
        private const string FILEDOWNLOADED_PT = "Arquivo baixado {0}\n...";

        private const string DOWNLOADINGPATCH_EN = "Step 1/{0}: Downloading patch-info...";
        private const string DOWNLOADINGPATCH_DE = "Schritt 1/{0}: Lade Patch-Info...";
        private const string DOWNLOADINGPATCH_PT = "Passo 1/{0}: Baixando informações do patch...";

        private const string SCANNINGFILES_EN = "Step 2/{0}: Comparing files...";
        private const string SCANNINGFILES_DE = "Schritt 2/{0}: Vergleiche Dateien...";
        private const string SCANNINGFILES_PT = "Passo 2/{0}: Comparando arquivos...";

        private const string DOWNLOADINIT_EN = "Step 3/{0}: Downloading update...";
        private const string DOWNLOADINIT_DE = "Schritt 3/{0}: Lade Update...";
        private const string DOWNLOADINIT_PT = "Passo 3/{0}: Baixando atualização...";

        private const string NGENINIT_EN = "Step 4/4: Optimizing startup...";
        private const string NGENINIT_DE = "Schritt 4/4: Optimiere Start...";
        private const string NGENINIT_PT = "Passo 4/4: Otimizando a inicialização...";

        private const string CLIENTUPTODATE_EN = "Client up to date, nothing to download";
        private const string CLIENTUPTODATE_DE = "Client ist auf dem neusten Stand";
        private const string CLIENTUPTODATE_PT = "Cliente atualizado, nada para baixar";

        private const string CLIENTWASUPDATED_EN = "Client update complete";
        private const string CLIENTWASUPDATED_DE = "Client wurde aktualisiert";
        private const string CLIENTWASUPDATED_PT = "Atualização do cliente concluída";

        private const string NUMFILES_EN = "{0}/{1} files";
        private const string NUMFILES_DE = "{0}/{1} Dateien";
        private const string NUMFILES_PT = "{0}/{1} arquivos";

        private const string UPDATEABORTED_EN = "Update aborted";
        private const string UPDATEABORTED_DE = "Update abgebrochen";
        private const string UPDATEABORTED_PT = "Atualização cancelada";

        private const string PROGRESSFINISHED_EN = "Complete";
        private const string PROGRESSFINISHED_DE = "Abgeschlossen";
        private const string PROGRESSFINISHED_PT = "Completo";

        private const string PROGRESSABORTED_EN = "Update aborted";
        private const string PROGRESSABORTED_DE = "Abgebrochen";
        private const string PROGRESSABORTED_PT = "Atualização abortada";
        #endregion Constants

        private LanguageIdentifier languageIdentifier;

        public LanguageHandler()
        {
            SetLanguageIdentifier();

            /*
            // As far as I know changing languages in Windows isn't possible
            // without a restart, but if it becomes necessary to handle then
            // I think this would catch the change.
            SystemEvents.UserPreferenceChanged += (sender, e) =>
            {
                // Regional settings have changed
                if (e.Category == UserPreferenceCategory.Locale)
                {
                    SetLanguageIdentifier();
                }
            };*/
        }

        private void SetLanguageIdentifier()
        {
            // CurrentUICulture should correspond to the user's OS language choice.
            CultureInfo currentCulture = CultureInfo.CurrentUICulture;

            if (currentCulture.TwoLetterISOLanguageName.Equals(CultureInfo.GetCultureInfo("de").TwoLetterISOLanguageName)
                || currentCulture.TwoLetterISOLanguageName.Equals(CultureInfo.GetCultureInfo("de-de").TwoLetterISOLanguageName))
            {
                languageIdentifier = LanguageIdentifier.German;
            }
            else if (currentCulture.TwoLetterISOLanguageName.Equals(CultureInfo.GetCultureInfo("pt").TwoLetterISOLanguageName)
                || currentCulture.TwoLetterISOLanguageName.Equals(CultureInfo.GetCultureInfo("pt-pt").TwoLetterISOLanguageName))
            {
                languageIdentifier = LanguageIdentifier.Portuguese;
            }
            else
            {
                // English for any other UI culture.
                languageIdentifier = LanguageIdentifier.English;
            }
        }

        public string ErrorText
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return ERROR_DE;
                    case LanguageIdentifier.Portuguese:
                        return ERROR_PT;
                    default:
                        return ERROR_EN;
                }
            }
        }

        public string AbortText
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return ABORT_DE;
                    case LanguageIdentifier.Portuguese:
                        return ABORT_PT;
                    default:
                        return ABORT_EN;
                }
            }
        }

        public string InfoText
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return INFO_DE;
                    case LanguageIdentifier.Portuguese:
                        return INFO_PT;
                    default:
                        return INFO_EN;
                }
            }
        }

        public string ConfirmCancel
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return CONFIRMCANCEL_DE;
                    case LanguageIdentifier.Portuguese:
                        return CONFIRMCANCEL_PT;
                    default:
                        return CONFIRMCANCEL_EN;
                }
            }
        }

        public string UrlInfoMissing
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return URLINFOMISSING_DE;
                    case LanguageIdentifier.Portuguese:
                        return URLINFOMISSING_PT;
                    default:
                        return URLINFOMISSING_EN;
                }
            }
        }

        public string UrlInfoBroken
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return URLINFOBROKEN_DE;
                    case LanguageIdentifier.Portuguese:
                        return URLINFOBROKEN_PT;
                    default:
                        return URLINFOBROKEN_EN;
                }
            }
        }

        public string JsonDownloadFailed
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return JSONDOWNLOADFAILED_DE;
                    case LanguageIdentifier.Portuguese:
                        return JSONDOWNLOADFAILED_PT;
                    default:
                        return JSONDOWNLOADFAILED_EN;
                }
            }
        }

        public string RetryingFile
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return RETRYINGFILE_DE;
                    case LanguageIdentifier.Portuguese:
                        return RETRYINGFILE_PT;
                    default:
                        return RETRYINGFILE_EN;
                }
            }
        }

        public string FileFailed
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return FILEFAILED_DE;
                    case LanguageIdentifier.Portuguese:
                        return FILEFAILED_PT;
                    default:
                        return FILEFAILED_EN;
                }
            }
        }

        public string PatchDownloadFailed
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return PATCHDOWNLOADFAILED_DE;
                    case LanguageIdentifier.Portuguese:
                        return PATCHDOWNLOADFAILED_PT;
                    default:
                        return PATCHDOWNLOADFAILED_EN;
                }
            }
        }

        public string FileDownloaded
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return FILEDOWNLOADED_DE;
                    case LanguageIdentifier.Portuguese:
                        return FILEDOWNLOADED_PT;
                    default:
                        return FILEDOWNLOADED_EN;
                }
            }
        }

        public string DownloadingPatch
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return DOWNLOADINGPATCH_DE;
                    case LanguageIdentifier.Portuguese:
                        return DOWNLOADINGPATCH_PT;
                    default:
                        return DOWNLOADINGPATCH_EN;
                }
            }
        }

        public string ScanningInit
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return SCANNINGFILES_DE;
                    case LanguageIdentifier.Portuguese:
                        return SCANNINGFILES_PT;
                    default:
                        return SCANNINGFILES_EN;
                }
            }
        }

        public string DownloadInit
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return DOWNLOADINIT_DE;
                    case LanguageIdentifier.Portuguese:
                        return DOWNLOADINIT_PT;
                    default:
                        return DOWNLOADINIT_EN;
                }
            }
        }

        public string NgenInit
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return NGENINIT_DE;
                    case LanguageIdentifier.Portuguese:
                        return NGENINIT_PT;
                    default:
                        return NGENINIT_EN;
                }
            }
        }
  
        public string ClientExecutableMissing
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return CLIENTEXEMISSINGERROR_DE;
                    case LanguageIdentifier.Portuguese:
                        return CLIENTEXEMISSINGERROR_PT;
                    default:
                        return CLIENTEXEMISSINGERROR_EN;
                }
            }
        }

        public string ClientUpToDate
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return CLIENTUPTODATE_DE;
                    case LanguageIdentifier.Portuguese:
                        return CLIENTUPTODATE_PT;
                    default:
                        return CLIENTUPTODATE_EN;
                }
            }
        }

        public string ClientWasUpdated
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return CLIENTWASUPDATED_DE;
                    case LanguageIdentifier.Portuguese:
                        return CLIENTWASUPDATED_PT;
                    default:
                        return CLIENTWASUPDATED_EN;
                }
            }
        }

        public string NumFiles
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return NUMFILES_DE;
                    case LanguageIdentifier.Portuguese:
                        return NUMFILES_PT;
                    default:
                        return NUMFILES_EN;
                }
            }
        }

        public string UpdateAborted
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return UPDATEABORTED_DE;
                    case LanguageIdentifier.Portuguese:
                        return UPDATEABORTED_PT;
                    default:
                        return UPDATEABORTED_EN;
                }
            }
        }

        public string ProgressFinished
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return PROGRESSFINISHED_DE;
                    case LanguageIdentifier.Portuguese:
                        return PROGRESSFINISHED_PT;
                    default:
                        return PROGRESSFINISHED_EN;
                }
            }
        }

        public string ProgressAborted
        {
            get
            {
                switch (languageIdentifier)
                {
                    case LanguageIdentifier.German:
                        return PROGRESSABORTED_DE;
                    case LanguageIdentifier.Portuguese:
                        return PROGRESSABORTED_PT;
                    default:
                        return PROGRESSABORTED_EN;
                }
            }
        }
    }
}
