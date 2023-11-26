#include "stdafx.h"

namespace Meridian59 { namespace Ogre
{
   void ControllerUI::ObjectDetails::Initialize()
   {
      // get windowmanager
      CEGUI::WindowManager* wndMgr = CEGUI::WindowManager::getSingletonPtr();

      // setup references to children from xml nodes
      Window      = static_cast<CEGUI::FrameWindow*>(guiRoot->getChild(UI_NAME_OBJECTDETAILS_WINDOW));
      Image       = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_OBJECTDETAILS_IMAGE));
      Name        = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_OBJECTDETAILS_NAME));
      Description = static_cast<CEGUI::MultiLineEditbox*>(Window->getChild(UI_NAME_OBJECTDETAILS_DESCRIPTION));
      Inscription = static_cast<CEGUI::MultiLineEditbox*>(Window->getChild(UI_NAME_OBJECTDETAILS_INSCRIPTION));
      OK          = static_cast<CEGUI::PushButton*>(Window->getChild(UI_NAME_OBJECTDETAILS_OK));

      // set initial layout type
      SetLayout(OgreClient::Singleton->Data->LookObject->LookType);

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

      // attach listener to lookobject
      OgreClient::Singleton->Data->LookObject->PropertyChanged += 
         gcnew PropertyChangedEventHandler(OnLookObjectPropertyChanged);

      // subscribe mouse wheel to image
      Image->subscribeEvent(CEGUI::Window::EventMouseWheel, CEGUI::Event::Subscriber(UICallbacks::ObjectDetails::OnImageMouseWheel));
      Image->subscribeEvent(CEGUI::Window::EventMouseClick, CEGUI::Event::Subscriber(UICallbacks::ObjectDetails::OnImageMouseClick));

      // subscribe OK button
      OK->subscribeEvent(CEGUI::PushButton::EventClicked, CEGUI::Event::Subscriber(UICallbacks::ObjectDetails::OnOKClicked));

      // subscribe keydown on description box and inscription
      Description->subscribeEvent(CEGUI::MultiLineEditbox::EventKeyDown, CEGUI::Event::Subscriber(UICallbacks::OnCopyPasteKeyDown));
      Inscription->subscribeEvent(CEGUI::MultiLineEditbox::EventKeyDown, CEGUI::Event::Subscriber(UICallbacks::OnCopyPasteKeyDown));

      // subscribe close button
      Window->subscribeEvent(CEGUI::FrameWindow::EventCloseClicked, CEGUI::Event::Subscriber(UICallbacks::ObjectDetails::OnWindowClosed));

      // subscribe keyup
      Window->subscribeEvent(CEGUI::FrameWindow::EventKeyUp, CEGUI::Event::Subscriber(UICallbacks::ObjectDetails::OnWindowKeyUp));
   };

   void ControllerUI::ObjectDetails::Destroy()
   {
      // detach listener to lookobject
      OgreClient::Singleton->Data->LookObject->PropertyChanged -= 
         gcnew PropertyChangedEventHandler(OnLookObjectPropertyChanged);

      // detach image listener
      imageComposer->NewImageAvailable -=
         gcnew ::System::EventHandler(OnNewImageAvailable);
   };

   void ControllerUI::ObjectDetails::ApplyLanguage()
   {
   };

   void ControllerUI::ObjectDetails::OnNewImageAvailable(Object^ sender, ::System::EventArgs^ e)
   {
      Image->setProperty(UI_PROPNAME_IMAGE, *imageComposer->Image->TextureName);
   };

   void ControllerUI::ObjectDetails::OnLookObjectPropertyChanged(Object^ sender, PropertyChangedEventArgs^ e)
   {
      // objectbase
      if (CLRString::Equals(e->PropertyName, ObjectInfo::PROPNAME_OBJECTBASE))
      {
         ObjectBase^ lookObject = OgreClient::Singleton->Data->LookObject->ObjectBase;

         // unset
         Image->setProperty(UI_PROPNAME_IMAGE, STRINGEMPTY);
         Name->setText(STRINGEMPTY);

         // possibly set to null
         imageComposer->DataSource = lookObject;	

         if (lookObject != nullptr)
         {
            // get color
            ::CEGUI::Colour color = ::CEGUI::Colour(
               NameColors::GetColorFor(lookObject->Flags));

            // set color and name
            Name->setProperty(UI_PROPNAME_NORMALTEXTCOLOUR, ::CEGUI::PropertyHelper<::CEGUI::Colour>::toString(color));
            Name->setText(StringConvert::CLRToCEGUI(lookObject->Name));
         }
      }

      // description
      else if (CLRString::Equals(e->PropertyName, ObjectInfo::PROPNAME_MESSAGE))
      {
         ServerString^ text = OgreClient::Singleton->Data->LookObject->Message;

         if (text != nullptr)
         {
            Description->setText(StringConvert::CLRToCEGUI(text->FullString));
            // possibly resize window to new text
            if (!OgreClient::Singleton->Data->LookObject->LookType->IsInscribed
               && !OgreClient::Singleton->Data->LookObject->LookType->IsEditable)
            {
               Window->setHeight(CEGUI::UDim(0, ControllerUI::GetAdjustedWindowHeightWithMLEB(Window, Description)));
            }
         }
      }

      // inscription
      else if (CLRString::Equals(e->PropertyName, ObjectInfo::PROPNAME_INSCRIPTION))
      {
         ServerString^ text = OgreClient::Singleton->Data->LookObject->Inscription;

         if (text != nullptr)
            Inscription->setText(StringConvert::CLRToCEGUI(text->FullString));
      }

      // looktype
      else if (CLRString::Equals(e->PropertyName, ObjectInfo::PROPNAME_LOOKTYPE))
      {
         LookTypeFlags^ type = OgreClient::Singleton->Data->LookObject->LookType;

         // set layout
         SetLayout(type);
      }

      // isvisible
      else if (CLRString::Equals(e->PropertyName, ObjectInfo::PROPNAME_ISVISIBLE))
      {
         // set window visibility
         Window->setVisible(OgreClient::Singleton->Data->LookObject->IsVisible);
         Window->moveToFront();
      }
   };

   void ControllerUI::ObjectDetails::SetLayout(LookTypeFlags^ LayoutType)
   {
      // some positioning
      CEGUI::UVector2 posImage = Image->getPosition();
      CEGUI::UVector2 posName = Name->getPosition();
      CEGUI::Sizef sizeImage = Image->getPixelSize();
      CEGUI::Sizef sizeName = Name->getPixelSize();

      float val1 = posImage.d_x.d_offset + sizeImage.d_width + (float)UI_DEFAULTPADDING;
      float val2 = posName.d_y.d_offset + sizeName.d_height + (float)UI_DEFAULTPADDING;

      // no inscription
      if (!LayoutType->IsInscribed && !LayoutType->IsEditable)
      {
         Inscription->setReadOnly(true);
         Inscription->setVisible(false);

         Description->setArea(
            CEGUI::UDim(0, val1),
            CEGUI::UDim(0, val2),
            CEGUI::UDim(1.0f, -val1 - (float)UI_DEFAULTPADDING),
            CEGUI::UDim(1.0f, -val2 - (float)UI_DEFAULTPADDING));

         OK->setVisible(false);
         Window->setHeight(CEGUI::UDim(0, ControllerUI::GetAdjustedWindowHeightWithMLEB(Window, Description)));
      }

      // non editable inscription
      else if (LayoutType->IsInscribed && !LayoutType->IsEditable)
      {
         Inscription->setReadOnly(true);
         Inscription->setVisible(true);

         Description->setArea(
            CEGUI::UDim(0, val1),
            CEGUI::UDim(0, val2),
            CEGUI::UDim(1.0f, -val1 - (float)UI_DEFAULTPADDING),
            CEGUI::UDim(0, sizeImage.d_height - sizeName.d_height - (float)UI_DEFAULTPADDING));

         OK->setVisible(false);
         Window->setHeight(CEGUI::UDim(0, 512.0f));
      }

      // editable inscription
      else
      {
         Inscription->setReadOnly(false);
         Inscription->setVisible(true);

         Description->setArea(
            CEGUI::UDim(0, val1),
            CEGUI::UDim(0, val2),
            CEGUI::UDim(1.0f, -val1 - (float)UI_DEFAULTPADDING),
            CEGUI::UDim(0, sizeImage.d_height - sizeName.d_height - (float)UI_DEFAULTPADDING));

         OK->setVisible(true);
         Window->setHeight(CEGUI::UDim(0, 512.0f));
      }
   };

   //////////////////////////////////////////////////////////////////////////////////////////////////////////
   //////////////////////////////////////////////////////////////////////////////////////////////////////////

   bool UICallbacks::ObjectDetails::OnImageMouseWheel(const CEGUI::EventArgs& e)
   {
      const CEGUI::MouseEventArgs& args = static_cast<const CEGUI::MouseEventArgs&>(e);
      ObjectBase^ lookObject = OgreClient::Singleton->Data->LookObject->ObjectBase;

      if (lookObject != nullptr)
         lookObject->ViewerAngle += (unsigned short)(args.wheelChange * 200.0f);

      return true;
   };

   bool UICallbacks::ObjectDetails::OnImageMouseClick(const CEGUI::EventArgs& e)
   {
      const CEGUI::MouseEventArgs& args = static_cast<const CEGUI::MouseEventArgs&>(e);
      ObjectBase^ lookObject = OgreClient::Singleton->Data->LookObject->ObjectBase;

      if (lookObject == nullptr)
         return true;

      // increment angle by this (each click is a new frame for 6 frames-groups)
      unsigned short increment = GeometryConstants::MAXANGLE / 6;

      // flip direction for right button
      if (args.button == CEGUI::MouseButton::RightButton)
         increment = -increment;

      // rotate
      lookObject->ViewerAngle += increment;

      return true;
   };

   bool UICallbacks::ObjectDetails::OnOKClicked(const CEGUI::EventArgs& e)
   {
      ObjectInfo^ lookInfo = OgreClient::Singleton->Data->LookObject;
      ObjectBase^ lookObj = lookInfo->ObjectBase;

      // don't to anything for non inscribed or non editable inscriptions
      if (!lookInfo->LookType->IsInscribed || !lookInfo->LookType->IsEditable)
         return true;

      // get text
      const ::CEGUI::String& inscription = ControllerUI::ObjectDetails::Inscription->getText();

      // convert to CLR
      CLRString^ clrInsc = StringConvert::CEGUIToCLR(inscription);

      // Remove ending newline added by CEGUI.
      if (clrInsc->EndsWith("\n"))
         clrInsc = clrInsc->Remove(clrInsc->Length - 1);

      // if text differs, send update to server
      if (!CLRString::Equals(lookInfo->Inscription->FullString, clrInsc))
         OgreClient::Singleton->SendChangeDescription(lookObj->ID, clrInsc);

      // hide
      OgreClient::Singleton->Data->LookObject->IsVisible = false;

      return true;
   };

   bool UICallbacks::ObjectDetails::OnWindowKeyUp(const CEGUI::EventArgs& e)
   {
      const CEGUI::KeyEventArgs& args = static_cast<const CEGUI::KeyEventArgs&>(e);

      // close window on ESC
      if (args.scancode == CEGUI::Key::Escape)
      {
         // clear (view will react)
         OgreClient::Singleton->Data->LookObject->IsVisible = false;
      }

      return UICallbacks::OnKeyUp(args);
   }

   bool UICallbacks::ObjectDetails::OnWindowClosed(const CEGUI::EventArgs& e)
   {
      // set not visible in datalayer (view will react)
      OgreClient::Singleton->Data->LookObject->IsVisible = false;

      // mark GUIroot active
      ControllerUI::ActivateRoot();

      return true;
   }
};};
