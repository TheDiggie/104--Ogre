#include "stdafx.h"

namespace Meridian59 { namespace Ogre
{
   void ControllerUI::PlayerDetails::Initialize()
   {
      // get windowmanager
      CEGUI::WindowManager& wndMgr = CEGUI::WindowManager::getSingleton();

      // setup references to children from xml nodes
      Window              = static_cast<CEGUI::FrameWindow*>(guiRoot->getChild(UI_NAME_PLAYERDETAILS_WINDOW));
      Image               = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_PLAYERDETAILS_IMAGE));
      Name                = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_PLAYERDETAILS_NAME));
      Titles              = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_PLAYERDETAILS_TITLES));
      Description         = static_cast<CEGUI::MultiLineEditbox*>(Window->getChild(UI_NAME_PLAYERDETAILS_DESCRIPTION));
      HomepageDescription = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_PLAYERDETAILS_HOMEPAGEDESC));
      HomepageValue       = static_cast<CEGUI::Editbox*>(Window->getChild(UI_NAME_PLAYERDETAILS_HOMEPAGEVAL));
      OK                  = static_cast<CEGUI::PushButton*>(Window->getChild(UI_NAME_PLAYERDETAILS_OK));

      // set server-side maximum string lengths
      Description->setMaxTextLength(BlakservStringLengths::MAX_CHAR_DESCRIPTION_LEN);
      HomepageValue->setMaxTextLength(BlakservStringLengths::MAX_URL_LEN);

      // image composer for picture
      imageComposer = gcnew ImageComposerCEGUI<ObjectBase^>();
      imageComposer->ApplyYOffset = false;
      imageComposer->IsScalePow2 = false;
      imageComposer->UseViewerFrame = true;
      imageComposer->Width = (unsigned int)Image->getPixelSize().d_width;
      imageComposer->Height = (unsigned int)Image->getPixelSize().d_height;
      imageComposer->CenterHorizontal = true;
      imageComposer->CenterVertical = true;
      imageComposer->NewImageAvailable += gcnew ::System::EventHandler(OnNewImageAvailable);

      // attach listener to Data
      OgreClient::Singleton->Data->LookPlayer->PropertyChanged += 
         gcnew PropertyChangedEventHandler(OnLookPlayerPropertyChanged);

      // subscribe mouse wheel to image
      Image->subscribeEvent(CEGUI::Window::EventMouseWheel, CEGUI::Event::Subscriber(UICallbacks::PlayerDetails::OnImageMouseWheel));
      Image->subscribeEvent(CEGUI::Window::EventMouseClick, CEGUI::Event::Subscriber(UICallbacks::PlayerDetails::OnImageMouseClick));

      // subscribe OK button
      OK->subscribeEvent(CEGUI::PushButton::EventClicked, CEGUI::Event::Subscriber(UICallbacks::PlayerDetails::OnOKClicked));

      // subscribe keydown on description box and homepage box
      Description->subscribeEvent(CEGUI::MultiLineEditbox::EventKeyDown, CEGUI::Event::Subscriber(UICallbacks::OnCopyPasteKeyDown));
      HomepageValue->subscribeEvent(CEGUI::Editbox::EventKeyDown, CEGUI::Event::Subscriber(UICallbacks::OnCopyPasteKeyDown));

      // subscribe close button
      Window->subscribeEvent(CEGUI::FrameWindow::EventCloseClicked, CEGUI::Event::Subscriber(UICallbacks::PlayerDetails::OnWindowClosed));

      // subscribe keyup
      Window->subscribeEvent(CEGUI::FrameWindow::EventKeyUp, CEGUI::Event::Subscriber(UICallbacks::PlayerDetails::OnWindowKeyUp));
   };

   void ControllerUI::PlayerDetails::Destroy()
   {
      // detach listener from Data
      OgreClient::Singleton->Data->LookPlayer->PropertyChanged -= 
         gcnew PropertyChangedEventHandler(OnLookPlayerPropertyChanged);

      imageComposer->NewImageAvailable -=
         gcnew ::System::EventHandler(OnNewImageAvailable);
   };

   void ControllerUI::PlayerDetails::ApplyLanguage()
   {
   };

   void ControllerUI::PlayerDetails::OnNewImageAvailable(Object^ sender, ::System::EventArgs^ e)
   {
      Image->setProperty(UI_PROPNAME_IMAGE, *imageComposer->Image->TextureName);
   };

   void ControllerUI::PlayerDetails::OnLookPlayerPropertyChanged(Object^ sender, PropertyChangedEventArgs^ e)
   {
      // objectbase
      if (CLRString::Equals(e->PropertyName, PlayerInfo::PROPNAME_OBJECTBASE))
      {
         ObjectBase^ lookPlayer = OgreClient::Singleton->Data->LookPlayer->ObjectBase;

         // unset
         Image->setProperty(UI_PROPNAME_IMAGE, STRINGEMPTY);
         Name->setText(STRINGEMPTY);

         // possibly set to null
         imageComposer->DataSource = lookPlayer;	

         if (lookPlayer != nullptr)
         {
            // get color
            ::CEGUI::Colour color = ::CEGUI::Colour(
               NameColors::GetColorFor(lookPlayer->Flags));

            // set color and name
            Name->setProperty(UI_PROPNAME_NORMALTEXTCOLOUR, ::CEGUI::PropertyHelper<::CEGUI::Colour>::toString(color));
            Name->setText(StringConvert::CLRToCEGUI(lookPlayer->Name));
         }
      }

      // titles
      else if (CLRString::Equals(e->PropertyName, PlayerInfo::PROPNAME_TITLES))
      {
#ifndef VANILLA
         CLRString^ text = OgreClient::Singleton->Data->LookPlayer->Titles->FullString;
#else
         CLRString^ text = OgreClient::Singleton->Data->LookPlayer->Titles;
#endif
         if (text != nullptr)
            Titles->setText(StringConvert::CLRToCEGUI(text));

         // count linebreaks
         int linebreaks = Common::Util::CountSubStringOccurences("\n", text, true);

         // get old height and calculate new
         float oldHeight = Titles->getHeight().d_offset;
         float newHeight = Titles->getFont()->getFontHeight() * (float)(linebreaks + 1);
         float dif = newHeight - oldHeight;

         // set new height
         Titles->setHeight(CEGUI::UDim(0, newHeight));

         // adjust description
         const CEGUI::URect areaDesc = Description->getArea();

         Description->setArea(
            areaDesc.left(), 
            CEGUI::UDim(0, areaDesc.top().d_offset + dif),
            areaDesc.getWidth(), 
            CEGUI::UDim(areaDesc.getHeight().d_scale, areaDesc.getHeight().d_offset - dif));
      }

      // description
      else if (CLRString::Equals(e->PropertyName, PlayerInfo::PROPNAME_MESSAGE))
      {
         ServerString^ text = OgreClient::Singleton->Data->LookPlayer->Message;

         if (text != nullptr)
            Description->setText(StringConvert::CLRToCEGUI(text->FullString));
      }

      // website
      else if (CLRString::Equals(e->PropertyName, PlayerInfo::PROPNAME_WEBSITE))
      {
         CLRString^ text = OgreClient::Singleton->Data->LookPlayer->Website;

         if (text != nullptr)
            HomepageValue->setText(StringConvert::CLRToCEGUI(text));
      }

      // iseditable
      else if (CLRString::Equals(e->PropertyName, PlayerInfo::PROPNAME_ISEDITABLE))
      {
         // set readonly settings
         Description->setReadOnly(!OgreClient::Singleton->Data->LookPlayer->IsEditable);
         HomepageValue->setReadOnly(!OgreClient::Singleton->Data->LookPlayer->IsEditable);
      }

      // isvisible
      else if (CLRString::Equals(e->PropertyName, PlayerInfo::PROPNAME_ISVISIBLE))
      {
         // set window visibility
         Window->setVisible(OgreClient::Singleton->Data->LookPlayer->IsVisible);
         Window->moveToFront();
      }
   };

   //////////////////////////////////////////////////////////////////////////////////////////////////////////
   //////////////////////////////////////////////////////////////////////////////////////////////////////////

   bool UICallbacks::PlayerDetails::OnImageMouseWheel(const CEGUI::EventArgs& e)
   {
      const CEGUI::MouseEventArgs& args = static_cast<const CEGUI::MouseEventArgs&>(e);
      ObjectBase^ lookPlayer = OgreClient::Singleton->Data->LookPlayer->ObjectBase;

      if (lookPlayer != nullptr)
         lookPlayer->ViewerAngle += (unsigned short)(args.wheelChange * 200.0f);

      return true;
   };

   bool UICallbacks::PlayerDetails::OnImageMouseClick(const CEGUI::EventArgs& e)
   {
      const CEGUI::MouseEventArgs& args = static_cast<const CEGUI::MouseEventArgs&>(e);
      ObjectBase^ lookPlayer = OgreClient::Singleton->Data->LookPlayer->ObjectBase;

      if (lookPlayer == nullptr)
         return true;

      // increment angle by this (each click is a new frame for 6 frames-groups)
      unsigned short increment = GeometryConstants::MAXANGLE / 6;

      // flip direction for right button
      if (args.button == CEGUI::MouseButton::RightButton)
         increment = -increment;

      // rotate
      lookPlayer->ViewerAngle += increment;

      return true;
   };

   bool UICallbacks::PlayerDetails::OnOKClicked(const CEGUI::EventArgs& e)
   {
      PlayerInfo^ lookInfo = OgreClient::Singleton->Data->LookPlayer;
      ObjectBase^ lookObj = lookInfo->ObjectBase;

      // check if we inspected our own avatar
      if (lookObj != nullptr && 
          lookObj->ID == OgreClient::Singleton->Data->AvatarID)
      {
         // get text
         const ::CEGUI::String& description = ControllerUI::PlayerDetails::Description->getText();

         // convert to CLR
         CLRString^ clrDesc = StringConvert::CEGUIToCLR(description);

         // Remove ending newline added by CEGUI.
         if (clrDesc->EndsWith("\n"))
            clrDesc = clrDesc->Remove(clrDesc->Length - 1);

         // if text differs, send update to server
         if (!CLRString::Equals(lookInfo->Message->FullString, clrDesc))
            OgreClient::Singleton->SendChangeDescription(clrDesc);


         // get website
         const ::CEGUI::String& website = ControllerUI::PlayerDetails::HomepageValue->getText();

         // convert to CLR
         CLRString^ clrWebsite = StringConvert::CEGUIToCLR(website);

         // Remove ending newline added by CEGUI.
         if (clrWebsite->EndsWith("\n"))
            clrWebsite = clrWebsite->Remove(clrWebsite->Length - 1);

         // if website differs, send update to server
         if (!CLRString::Equals(lookInfo->Website, clrWebsite))
            OgreClient::Singleton->SendUserCommandChangeURL(clrWebsite);
      }

      // hide
      OgreClient::Singleton->Data->LookPlayer->IsVisible = false;

      return true;
   };

   bool UICallbacks::PlayerDetails::OnWindowKeyUp(const CEGUI::EventArgs& e)
   {
      const CEGUI::KeyEventArgs& args = static_cast<const CEGUI::KeyEventArgs&>(e);

      // close window on ESC
      if (args.scancode == CEGUI::Key::Escape)
      {
         // clear (view will react)
         OgreClient::Singleton->Data->LookPlayer->IsVisible = false;

         // mark GUIroot active
         ControllerUI::ActivateRoot();
      }

      return true;
   };

   bool UICallbacks::PlayerDetails::OnWindowClosed(const CEGUI::EventArgs& e)
   {
      // set not visible in datalayer (view will react)
      OgreClient::Singleton->Data->LookPlayer->IsVisible = false;

      // mark GUIroot active
      ControllerUI::ActivateRoot();

      return true;
   };
};};
