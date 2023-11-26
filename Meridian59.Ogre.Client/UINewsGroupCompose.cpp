#include "stdafx.h"

namespace Meridian59 { namespace Ogre
{
   void ControllerUI::NewsGroupCompose::Initialize()
   {
      // setup references to children from xml nodes
      Window       = static_cast<CEGUI::FrameWindow*>(guiRoot->getChild(UI_NAME_NEWSGROUPCOMPOSE_WINDOW));
      GroupDesc    = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_NEWSGROUPCOMPOSE_GROUPDESC));
      Group        = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_NEWSGROUPCOMPOSE_GROUP));
      HeadLineDesc = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_NEWSGROUPCOMPOSE_HEADLINEDESC));
      HeadLine     = static_cast<CEGUI::Editbox*>(Window->getChild(UI_NAME_NEWSGROUPCOMPOSE_HEADLINE));		
      Text         = static_cast<CEGUI::MultiLineEditbox*>(Window->getChild(UI_NAME_NEWSGROUPCOMPOSE_TEXT));
      Send         = static_cast<CEGUI::PushButton*>(Window->getChild(UI_NAME_NEWSGROUPCOMPOSE_SEND));

      // set maximum length for title and body (check with server values...)
      HeadLine->setMaxTextLength(BlakservStringLengths::NEWS_POSTING_MAX_SUBJECT_LENGTH);
      Text->setMaxTextLength(BlakservStringLengths::NEWS_POSTING_MAX_LENGTH-1);

      // attach listener to newsgroup data
      OgreClient::Singleton->Data->NewsGroup->PropertyChanged += 
         gcnew PropertyChangedEventHandler(OnNewsGroupPropertyChanged);

      // subscribe send button
      Send->subscribeEvent(CEGUI::PushButton::EventClicked, CEGUI::Event::Subscriber(UICallbacks::NewsGroupCompose::OnSendClicked));

      // subscribe close button
      Window->subscribeEvent(CEGUI::FrameWindow::EventCloseClicked, CEGUI::Event::Subscriber(UICallbacks::OnWindowClosed));

      // subscribe keydown on headline box and text
      HeadLine->subscribeEvent(CEGUI::Editbox::EventKeyDown, CEGUI::Event::Subscriber(UICallbacks::OnCopyPasteKeyDown));
      Text->subscribeEvent(CEGUI::MultiLineEditbox::EventKeyDown, CEGUI::Event::Subscriber(UICallbacks::OnCopyPasteKeyDown));

      // block chatpopup on return
      HeadLine->subscribeEvent(CEGUI::Editbox::EventKeyUp, CEGUI::Event::Subscriber(UICallbacks::OnKeyUpBlock));
      Text->subscribeEvent(CEGUI::MultiLineEditbox::EventKeyUp, CEGUI::Event::Subscriber(UICallbacks::OnKeyUpBlock));

      // subscribe keyup
      Window->subscribeEvent(CEGUI::FrameWindow::EventKeyUp, CEGUI::Event::Subscriber(UICallbacks::OnKeyUp));
   };

   void ControllerUI::NewsGroupCompose::Destroy()
   {
      // detach listener from newsgroup data
      OgreClient::Singleton->Data->NewsGroup->PropertyChanged -= 
         gcnew PropertyChangedEventHandler(OnNewsGroupPropertyChanged);
   };

   void ControllerUI::NewsGroupCompose::ApplyLanguage()
   {
   };

   void ControllerUI::NewsGroupCompose::OnNewsGroupPropertyChanged(Object^ sender, PropertyChangedEventArgs^ e)
   {
      Data::Models::NewsGroup^ obj = OgreClient::Singleton->Data->NewsGroup;

      // visible
      if (CLRString::Equals(e->PropertyName, Data::Models::NewsGroup::PROPNAME_ISVISIBLE))
      {
         // close also with newsgroup window, but don't open together
         if (!obj->IsVisible)
         {
            // hide window
            Window->hide();

            // focusback to root
            ControllerUI::ActivateRoot();
         }
      }

      // globeobject
      else if (CLRString::Equals(e->PropertyName, Data::Models::NewsGroup::PROPNAME_NEWSGLOBEOBJECT))
      {
         if (obj->NewsGlobeObject != nullptr)
            Group->setText(StringConvert::CLRToCEGUI(obj->NewsGlobeObject->Name));
      }
   };

   //////////////////////////////////////////////////////////////////////////////////////////////////////////
   //////////////////////////////////////////////////////////////////////////////////////////////////////////

   bool UICallbacks::NewsGroupCompose::OnSendClicked(const CEGUI::EventArgs& e)
   {
      const CEGUI::WindowEventArgs& args	= (const CEGUI::WindowEventArgs&)e;

      // id of currently viewed newsgroup we're responding to
      unsigned short id = OgreClient::Singleton->Data->NewsGroup->NewsGlobeID;

      // convert userinput to managed strings
      CLRString^ title = StringConvert::CEGUIToCLR(ControllerUI::NewsGroupCompose::HeadLine->getText());
      CLRString^ text = StringConvert::CEGUIToCLR(ControllerUI::NewsGroupCompose::Text->getText());

      // send to server
      OgreClient::Singleton->SendPostArticle(id, title, text);

      // rerequest newsgroup articles
      OgreClient::Singleton->SendReqArticles();

      // hide compose window
      ControllerUI::NewsGroupCompose::Window->setVisible(false);

      // mark GUIroot active
      ControllerUI::ActivateRoot();

      // clear out sent text
      ControllerUI::NewsGroupCompose::HeadLine->setText(STRINGEMPTY);
      ControllerUI::NewsGroupCompose::Text->setText(STRINGEMPTY);

      return true;
   };
};};
