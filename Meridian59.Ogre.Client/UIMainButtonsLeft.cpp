#include "stdafx.h"

namespace Meridian59 { namespace Ogre
{
   void ControllerUI::MainButtonsLeft::Initialize()
   {
      // get windowmanager
      CEGUI::WindowManager* wndMgr = CEGUI::WindowManager::getSingletonPtr();

      // setup references to children from xml nodes
      Window    = static_cast<CEGUI::Window*>(guiRoot->getChild(UI_NAME_MAINBUTTONSLEFT_WINDOW));
      Chat      = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_MAINBUTTONSLEFT_CHAT));
      Inventory = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_MAINBUTTONSLEFT_INVENTORY));
      Map       = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_MAINBUTTONSLEFT_MAP));
      Spells    = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_MAINBUTTONSLEFT_SPELLS));
      Skills    = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_MAINBUTTONSLEFT_SKILLS));
      Actions   = static_cast<CEGUI::Window*>(Window->getChild(UI_NAME_MAINBUTTONSLEFT_ACTIONS));

      // set window layout from config
      Window->setPosition(OgreClient::Singleton->Config->UILayoutMainButtonsLeft->getPosition());
      Window->setSize(OgreClient::Singleton->Config->UILayoutMainButtonsLeft->getSize());

      // subscribe click to head
      Chat->subscribeEvent(CEGUI::Window::EventMouseClick, CEGUI::Event::Subscriber(UICallbacks::MainButtonsLeft::OnItemClicked));
      Inventory->subscribeEvent(CEGUI::Window::EventMouseClick, CEGUI::Event::Subscriber(UICallbacks::MainButtonsLeft::OnItemClicked));
      Map->subscribeEvent(CEGUI::Window::EventMouseClick, CEGUI::Event::Subscriber(UICallbacks::MainButtonsLeft::OnItemClicked));
      Spells->subscribeEvent(CEGUI::Window::EventMouseClick, CEGUI::Event::Subscriber(UICallbacks::MainButtonsLeft::OnItemClicked));
      Skills->subscribeEvent(CEGUI::Window::EventMouseClick, CEGUI::Event::Subscriber(UICallbacks::MainButtonsLeft::OnItemClicked));
      Actions->subscribeEvent(CEGUI::Window::EventMouseClick, CEGUI::Event::Subscriber(UICallbacks::MainButtonsLeft::OnItemClicked));

      // subscribe mouse events
      Window->subscribeEvent(CEGUI::Window::EventMouseButtonDown, CEGUI::Event::Subscriber(UICallbacks::MainButtonsLeft::OnMouseDown));
      Window->subscribeEvent(CEGUI::Window::EventMouseButtonUp, CEGUI::Event::Subscriber(UICallbacks::MainButtonsLeft::OnMouseUp));
   };

   void ControllerUI::MainButtonsLeft::Destroy()
   {
   };

   void ControllerUI::MainButtonsLeft::ApplyLanguage()
   {
   };

   //////////////////////////////////////////////////////////////////////////////////////////////////////////
   //////////////////////////////////////////////////////////////////////////////////////////////////////////

   bool UICallbacks::MainButtonsLeft::OnItemClicked(const CEGUI::EventArgs& e)
   {
      const CEGUI::MouseEventArgs& args = static_cast<const CEGUI::MouseEventArgs&>(e);

      // chat clicked
      if (args.window == ControllerUI::MainButtonsLeft::Chat)
      {
         ControllerUI::ToggleVisibility(ControllerUI::Chat::Window);
      }

      // inventory clicked
      else if (args.window == ControllerUI::MainButtonsLeft::Inventory)
      {
         ControllerUI::ToggleVisibility(ControllerUI::Inventory::Window);
      }

      // map clicked
      else if (args.window == ControllerUI::MainButtonsLeft::Map)
      {
         ControllerUI::ToggleVisibility(ControllerUI::MiniMap::Window);
      }

      // spells clicked
      else if (args.window == ControllerUI::MainButtonsLeft::Spells)
      {
         ControllerUI::ToggleVisibility(ControllerUI::Spells::Window);
      }

      // skills clicked
      else if (args.window == ControllerUI::MainButtonsLeft::Skills)
      {
         ControllerUI::ToggleVisibility(ControllerUI::Skills::Window);
      }

      // actions clicked
      else if (args.window == ControllerUI::MainButtonsLeft::Actions)
      {
         ControllerUI::ToggleVisibility(ControllerUI::Actions::Window);
      }

      return true;
   };

   bool UICallbacks::MainButtonsLeft::OnMouseDown(const CEGUI::EventArgs& e)
   {
      const CEGUI::MouseEventArgs& args = static_cast<const CEGUI::MouseEventArgs&>(e);

      // set this window as moving one
      ControllerUI::MovingWindow = ControllerUI::MainButtonsLeft::Window;

      return true;
   };

   bool UICallbacks::MainButtonsLeft::OnMouseUp(const CEGUI::EventArgs& e)
   {
      const CEGUI::MouseEventArgs& args = static_cast<const CEGUI::MouseEventArgs&>(e);

      // unset this window as moving one
      ControllerUI::MovingWindow = nullptr;

      return true;
   };
};};
