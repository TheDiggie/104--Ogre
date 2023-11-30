#include "stdafx.h"

namespace Meridian59 { namespace Ogre
{
   void ControllerUI::Branding::Initialize()
   {
      // setup references to children from xml nodes
      Logo = static_cast<CEGUI::FrameWindow*>(guiRoot->getChild(UI_NAME_BRANDINGLOGO_WINDOW));
      Text = static_cast<CEGUI::FrameWindow*>(guiRoot->getChild(UI_NAME_BRANDINGLOGO_TEXT));

      // get version as string
      const CEGUI::String strVersion = StringConvert::CLRToCEGUI(
         ::System::Reflection::Assembly::GetExecutingAssembly()->GetName()->Version->ToString());
       
      // get x64/x86 as string
      const CEGUI::String strArch = ::System::Environment::Is64BitProcess ? "64-bit" : "32-bit";

      // build branding base text with version, release/debug and 32/64 bit
      const CEGUI::String baseStr =
         "Version " + strVersion + "\n" +
         UI_BUILDTYPE + " (" + strArch + ")\n";

      // set according branding logo and text
      Logo->setProperty(UI_PROPNAME_IMAGE, UI_IMAGE_BRANDING);
      Text->setText(baseStr + "Server 104");
   };

   void ControllerUI::Branding::Destroy()
   {
   };
};};
