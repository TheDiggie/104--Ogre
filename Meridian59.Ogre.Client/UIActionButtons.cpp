#include "stdafx.h"

namespace Meridian59 { namespace Ogre
{
   void ControllerUI::ActionButtons::Initialize()
   {
      // get windowmanager
      CEGUI::WindowManager* wndMgr = CEGUI::WindowManager::getSingletonPtr();

      // setup references to children from xml nodes
      Window	= static_cast<CEGUI::Window*>(guiRoot->getChild(UI_NAME_ACTIONBUTTONS_WINDOW));
      Grid	= static_cast<CEGUI::GridLayoutContainer*>(Window->getChild(UI_NAME_ACTIONBUTTONS_GRID));

      // set window layout from config
      Window->setPosition(OgreClient::Singleton->Config->UILayoutActionButtons->getPosition());
      Window->setSize(OgreClient::Singleton->Config->UILayoutActionButtons->getSize());

      // attach listener to actionbutton models
      OgreClient::Singleton->Data->ActionButtons->ListChanged += 
         gcnew ListChangedEventHandler(OnActionButtonsListChanged);

      // amount of entries the buffgrid can hold
      const int entries = UI_ACTIONBUTTONS_COLS * UI_ACTIONBUTTONS_ROWS;

      // set dimension (no. of items per row and no. of cols)
      Grid->setGridDimensions(UI_ACTIONBUTTONS_COLS, UI_ACTIONBUTTONS_ROWS);

      // create image composers for slots
      imageComposers = gcnew array<ImageComposerCEGUI<ObjectBase^>^>(entries);

      for(int i = 0; i < entries; i++)
      {
         imageComposers[i] = gcnew ImageComposerCEGUI<ObjectBase^>();
         imageComposers[i]->ApplyYOffset = false;
         imageComposers[i]->HotspotIndex = 0;
         imageComposers[i]->IsScalePow2 = false;
         imageComposers[i]->UseViewerFrame = false;
         imageComposers[i]->Width = UI_ACTIONBUTTON_WIDTH;
         imageComposers[i]->Height = UI_ACTIONBUTTON_HEIGHT;
         imageComposers[i]->CenterHorizontal = true;
         imageComposers[i]->CenterVertical = true;
         imageComposers[i]->NewImageAvailable += gcnew ::System::EventHandler(OnNewImageAvailable);
      }

      // create imagebuttons in slots
      for(int i = 0; i < entries; i++)
      {
         // create widget
         CEGUI::Window* widget = (CEGUI::Window*)wndMgr->createWindow(UI_WINDOWTYPE_ACTIONBUTTON);
         CEGUI::DragContainer* dragger = (CEGUI::DragContainer*)wndMgr->createWindow("DragContainer");

         // size of elements
         CEGUI::USize size = CEGUI::USize(
            CEGUI::UDim(0, UI_ACTIONBUTTON_WIDTH + 5), 
            CEGUI::UDim(0, UI_ACTIONBUTTON_HEIGHT + 5));

         CEGUI::USize size2 = CEGUI::USize(
            CEGUI::UDim(0, UI_ACTIONBUTTON_WIDTH), 
            CEGUI::UDim(0, UI_ACTIONBUTTON_HEIGHT));

         // some settings
         dragger->setSize(size);
         dragger->setMouseInputPropagationEnabled(false);
         dragger->setMouseCursor(UI_DEFAULTARROW);

         widget->setSize(size2);
         widget->setPosition(CEGUI::UVector2(CEGUI::UDim(0, 2.5f), CEGUI::UDim(0, 2.5f)));
         widget->setProperty(UI_PROPNAME_NORMALTEXTCOLOUR, UI_COLOR_RED);
         widget->setProperty(UI_PROPNAME_FRAMEENABLED, "False");
         widget->setProperty(UI_PROPNAME_BACKGROUNDENABLED, "False");

         widget->setMousePassThroughEnabled(true);
         widget->setFont(UI_FONT_LIBERATIONSANS10B);

#ifdef _DEBUG
         widget->setText(CEGUI::PropertyHelper<int>::toString(i));
#endif
         // subscribe dragend
         dragger->subscribeEvent(CEGUI::DragContainer::EventDragStarted, CEGUI::Event::Subscriber(UICallbacks::ActionButtons::OnDragStarted));
         dragger->subscribeEvent(CEGUI::DragContainer::EventDragEnded, CEGUI::Event::Subscriber(UICallbacks::ActionButtons::OnDragEnded));
         dragger->subscribeEvent(CEGUI::DragContainer::EventDragDropItemDropped, CEGUI::Event::Subscriber(UICallbacks::ActionButtons::OnItemDropped));
         dragger->subscribeEvent(CEGUI::DragContainer::EventMouseClick, CEGUI::Event::Subscriber(UICallbacks::ActionButtons::OnItemClicked));

         // add
         dragger->addChild(widget);
         Grid->addChild(dragger);
      }

      // subscribe mouse events
      Window->subscribeEvent(CEGUI::Window::EventMouseButtonDown, CEGUI::Event::Subscriber(UICallbacks::ActionButtons::OnMouseDown));
      Window->subscribeEvent(CEGUI::Window::EventMouseButtonUp, CEGUI::Event::Subscriber(UICallbacks::ActionButtons::OnMouseUp));
   };

   void ControllerUI::ActionButtons::Destroy()
   {
      // amount of entries the buffgrid can hold
      const int entries = UI_ACTIONBUTTONS_COLS * UI_ACTIONBUTTONS_ROWS;

      for(int i = 0; i < entries; i++)		
         imageComposers[i]->NewImageAvailable -= gcnew ::System::EventHandler(OnNewImageAvailable);
   };

   void ControllerUI::ActionButtons::ApplyLanguage()
   {
      // not sure, if we need this function here...
   };

   void ControllerUI::ActionButtons::OnNewImageAvailable(Object^ sender, ::System::EventArgs^ e)
   {
      ImageComposerCEGUI<ObjectBase^>^ imageComposer = (ImageComposerCEGUI<ObjectBase^>^)sender;
      int index = ::System::Array::IndexOf(imageComposers, imageComposer);

      if ((int)Grid->getChildCount() > index)
      {
         // get imagebutton
         CEGUI::Window* imgButton = Grid->getChildAtIdx(index)->getChildAtIdx(0);

         // set updated image
         imgButton->setProperty(UI_PROPNAME_IMAGE, *imageComposer->Image->TextureName); 
      }
   };

   void ControllerUI::ActionButtons::OnActionButtonsListChanged(Object^ sender, ListChangedEventArgs^ e)
   {
      switch(e->ListChangedType)
      {
      case ::System::ComponentModel::ListChangedType::ItemAdded:
         ActionButtonAdd(e->NewIndex);
         break;

      case ::System::ComponentModel::ListChangedType::ItemDeleted:
         ActionButtonRemove(e->NewIndex);
         break;

      case ::System::ComponentModel::ListChangedType::ItemChanged:
         ActionButtonChange(e->NewIndex);
         break;
      }
   };

   void ControllerUI::ActionButtons::ActionButtonAdd(int Index)
   {
      // get new datamodel entry
      /*ActionButtonConfig^ button = OgreClient::Singleton->Data->ActionButtons[Index];

      // if we have that many slots..
      if ((int)Grid->getChildCount() > Index &&
         imageComposers->Length > Index)
      {
         // get imagebutton
         CEGUI::Window* imgButton = Grid->getChildAtIdx(Index)->getChildAtIdx(0);
      }*/

      // update values
      ActionButtonChange(Index);
   };

   void ControllerUI::ActionButtons::ActionButtonRemove(int Index)
   {
      // check
      //if ((int)Conditions->getChildCount() > Index)
      // Conditions->removeChildFromPosition(Index);
   };

   void ControllerUI::ActionButtons::ActionButtonChange(int Index)
   {
      ActionButtonConfig^ dataModel = OgreClient::Singleton->Data->ActionButtons[Index];
      imageComposers[Index]->DataSource = nullptr;

      // check
      if ((int)Grid->getChildCount() > Index)
      {
         // get imagebutton
         CEGUI::DragContainer* dragger = (CEGUI::DragContainer*)Grid->getChildAtIdx(Index);
         CEGUI::Window* imgButton = dragger->getChildAtIdx(0);

         // set tooltip
         const char* KEY = GetLangLabel(LANGSTR::KEY);
         const ::CEGUI::String& label = ::CEGUI::String(KEY) + ": " + StringConvert::CLRToCEGUI(dataModel->Label);
         const ::CEGUI::String& s = (!dataModel->Name || dataModel->Name == "") ? label :
            StringConvert::CLRToCEGUI(dataModel->Name) + "\n" + label;

         dragger->setTooltipText(s);

         if (dataModel->ButtonType == ActionButtonType::Unset)
         {
            imgButton->setProperty(UI_PROPNAME_IMAGE, STRINGEMPTY);
            dragger->setMouseCursor(UI_DEFAULTARROW);
         }

         else if (dataModel->ButtonType == ActionButtonType::Action)
         {
            dragger->setMouseCursor(UI_MOUSECURSOR_HAND);
            AvatarAction action = (AvatarAction)dataModel->Data;

            switch(action)
            {
            case AvatarAction::Activate:
               imgButton->setProperty(UI_PROPNAME_IMAGE, UI_IMAGE_ACTION_ACTIVATE); 
               break;

            case AvatarAction::Attack:
               imgButton->setProperty(UI_PROPNAME_IMAGE, UI_IMAGE_ACTION_ATTACK); 
               break;

            case AvatarAction::Buy:
               imgButton->setProperty(UI_PROPNAME_IMAGE, UI_IMAGE_ACTION_BUY); 
               break;

            case AvatarAction::Dance:
               imgButton->setProperty(UI_PROPNAME_IMAGE, UI_IMAGE_ACTION_DANCE); 
               break;

            case AvatarAction::Inspect:
               imgButton->setProperty(UI_PROPNAME_IMAGE, UI_IMAGE_ACTION_INSPECT); 
               break;

            case AvatarAction::Loot:
               imgButton->setProperty(UI_PROPNAME_IMAGE, UI_IMAGE_ACTION_LOOT); 
               break;

            case AvatarAction::Point:
               imgButton->setProperty(UI_PROPNAME_IMAGE, UI_IMAGE_ACTION_POINT); 
               break;

            case AvatarAction::Rest:
               imgButton->setProperty(UI_PROPNAME_IMAGE, UI_IMAGE_ACTION_REST); 
               break;

            case AvatarAction::Trade:
               imgButton->setProperty(UI_PROPNAME_IMAGE, UI_IMAGE_ACTION_TRADE); 
               break;

            case AvatarAction::Wave:
               imgButton->setProperty(UI_PROPNAME_IMAGE, UI_IMAGE_ACTION_WAVE); 
               break;

            case AvatarAction::GuildInvite:
               imgButton->setProperty(UI_PROPNAME_IMAGE, UI_IMAGE_ACTION_GUILDINVITE);
               break;
            }
         }

         else if (dataModel->ButtonType == ActionButtonType::Item)
         {
            dragger->setMouseCursor(UI_MOUSECURSOR_HAND);

            if (dataModel->Data)
               imageComposers[Index]->DataSource = (InventoryObject^)dataModel->Data;
         }

         else if (dataModel->ButtonType == ActionButtonType::Spell)
         {
            dragger->setMouseCursor(UI_MOUSECURSOR_HAND);

            //imageComposers[Index]->DataSource = (SpellObject^)dataModel->Data;

            // hack: use higher resolution if available
            // instead of attaching spellobject to imagecomposer
            SpellObject^ spellObject = (SpellObject^)dataModel->Data;

            // set image if available
            if (spellObject != nullptr && spellObject->Resource != nullptr && spellObject->Resource->Frames->Count > 0)
            {
               int index = (spellObject->Resource->Frames->Count > 1) ? 1 : 0;

               Ogre::TextureManager& texMan = Ogre::TextureManager::getSingleton();

               // build name
               const ::Ogre::String& oStrName = 
                  StringConvert::CLRToOgre(UI_NAMEPREFIX_STATICICON + spellObject->OverlayFile + "/" + index.ToString());

               // possibly create texture
               Util::CreateTextureA8R8G8B8(
                  spellObject->Resource->Frames[index], oStrName, 
                  UI_RESGROUP_IMAGESETS, 
                  MIP_DEFAULT);

               // reget TexPtr (no return from function works, ugh..)
               TexturePtr texPtr = texMan.getByName(oStrName);

               if (texPtr)
               {
                  // possibly create cegui wrap around it
                  Util::CreateCEGUITextureFromOgre(ControllerUI::Renderer, texPtr);

                  // set image
                  imgButton->setProperty(UI_PROPNAME_IMAGE, oStrName);
               }
            }
         }

         else if (dataModel->ButtonType == ActionButtonType::Skill)
         {
            dragger->setMouseCursor(UI_MOUSECURSOR_HAND);

            //imageComposers[Index]->DataSource = (SkillObject^)dataModel->Data;

            // hack: use higher resolution if available
            // instead of attaching skillobject to imagecomposer
            SkillObject^ skillObject = (SkillObject^)dataModel->Data;

            // set image if available
            if (skillObject != nullptr && skillObject->Resource != nullptr && skillObject->Resource->Frames->Count > 0)
            {
               int index = (skillObject->Resource->Frames->Count > 1) ? 1 : 0;

               Ogre::TextureManager& texMan = Ogre::TextureManager::getSingleton();

               // build name
               const ::Ogre::String& oStrName =
                  StringConvert::CLRToOgre(UI_NAMEPREFIX_STATICICON + skillObject->OverlayFile + "/" + index.ToString());

               // possibly create texture
               Util::CreateTextureA8R8G8B8(
                  skillObject->Resource->Frames[index], oStrName,
                  UI_RESGROUP_IMAGESETS,
                  MIP_DEFAULT);

               // reget TexPtr (no return from function works, ugh..)
               TexturePtr texPtr = texMan.getByName(oStrName);

               if (texPtr)
               {
                  // possibly create cegui wrap around it
                  Util::CreateCEGUITextureFromOgre(ControllerUI::Renderer, texPtr);

                  // set image
                  imgButton->setProperty(UI_PROPNAME_IMAGE, oStrName);
               }
            }
         }

         else if (dataModel->ButtonType == ActionButtonType::Alias)
         {
            dragger->setMouseCursor(UI_MOUSECURSOR_HAND);
            imgButton->setProperty(UI_PROPNAME_IMAGE, UI_IMAGE_ALIAS_ICON);
         }
      }
   };

   //////////////////////////////////////////////////////////////////////////////////////////////////////////
   //////////////////////////////////////////////////////////////////////////////////////////////////////////

   bool UICallbacks::ActionButtons::OnItemClicked(const CEGUI::EventArgs& e)
   {
      const CEGUI::MouseEventArgs& args      = (const CEGUI::MouseEventArgs&)e;
      const CEGUI::GridLayoutContainer* grid = ControllerUI::ActionButtons::Grid;

      // get index of clicked slot
      int index = (int)grid->getIdxOfChild(args.window);

      // activate datamodel at index
      OgreClient::Singleton->Data->ActionButtons[index]->Activate();

      return true;
   };

   bool UICallbacks::ActionButtons::OnItemDropped(const CEGUI::EventArgs& e)
   {
      const CEGUI::DragDropEventArgs& args            = (const CEGUI::DragDropEventArgs&)e;
      const CEGUI::GridLayoutContainer* gridButtons   = ControllerUI::ActionButtons::Grid;
      const CEGUI::GridLayoutContainer* gridInventory = ControllerUI::Inventory::List;
      const CEGUI::ItemListbox* listSpells            = ControllerUI::Spells::List;
      const CEGUI::ItemListbox* listSkills            = ControllerUI::Skills::List;
      const CEGUI::ItemListbox* listActions           = ControllerUI::Actions::List;
      const CEGUI::ItemListbox* listAliases           = ControllerUI::Options::ListAliases;

      ActionButtonList^ buttonModels       = OgreClient::Singleton->Data->ActionButtons;
      InventoryObjectList^ inventoryModels = OgreClient::Singleton->Data->InventoryObjects;
      SpellObjectList^ spellModels         = OgreClient::Singleton->Data->SpellObjects;
      SkillObjectList^ skillModels         = OgreClient::Singleton->Data->SkillObjects;
      KeyValuePairStringList^ aliasModels  = OgreClient::Singleton->Config->Aliases;

      // find index of droptarget
      int indexbutton = (int)gridButtons->getIdxOfChild(args.window);

      // get parent hierarchy
      CEGUI::Window* parent = args.dragDropItem->getParent();
      CEGUI::Window* parent2 = parent->getParent();
      CEGUI::Window* parent3 = parent2->getParent();

      // from inventory (it's parent there)
      if (parent == gridInventory)
      {
         // find index of dropsource
         int indexitem = (int)gridInventory->getIdxOfChild(args.dragDropItem);

         if (inventoryModels->Count > indexitem)
            buttonModels[indexbutton]->SetToItem(inventoryModels[indexitem]);
      }

      // from spells
      else if (parent3 == listSpells)
      {
         // find index of dropsource
         int indexitem = (int)listSpells->getItemIndex((CEGUI::ItemEntry*)parent);

         if (spellModels->Count > indexitem)
            buttonModels[indexbutton]->SetToSpell(spellModels[indexitem]);
      }

      // from skills
      else if (parent3 == listSkills)
      {
         // find index of dropsource
         int indexitem = (int)listSkills->getItemIndex((CEGUI::ItemEntry*)parent);

         if (skillModels->Count > indexitem)
            buttonModels[indexbutton]->SetToSkill(skillModels[indexitem]);
      }

      // from actions
      else if (parent3 == listActions)
      {
         // find index of dropsource
         int indexitem = (int)listActions->getItemIndex(
            (CEGUI::ItemEntry*)parent);

         CEGUI::String actionStr = listActions->getItemFromIndex(indexitem)->getChildAtIdx(
            UI_ACTIONS_CHILDINDEX_NAME)->getText();

         buttonModels[indexbutton]->SetToAction(
            ActionButtonConfig::GetAction(StringConvert::CEGUIToCLR(actionStr)));
      }

      // from aliases
      else if (parent3 == listAliases)
      {
         // find index of dropsource
         int index = (int)listAliases->getItemIndex((CEGUI::ItemEntry*)parent);

         if (aliasModels->Count > index)
            buttonModels[indexbutton]->SetToAlias(aliasModels[index]);
      }

      return true;
   }

   bool UICallbacks::ActionButtons::OnDragStarted(const CEGUI::EventArgs& e)
   {
      const CEGUI::WindowEventArgs& args = static_cast<const CEGUI::WindowEventArgs&>(e);

      CEGUI::DragContainer* drag = (CEGUI::DragContainer*)args.window;

      if (!drag->isDraggingEnabled())
         return true;

      ControllerUI::ActionButtons::Window->setUsingAutoRenderingSurface(false);

      return true;
   };

   bool UICallbacks::ActionButtons::OnDragEnded(const CEGUI::EventArgs& e)
   {
      const CEGUI::WindowEventArgs& args     = (const CEGUI::WindowEventArgs&)e;
      CEGUI::DragContainer* dragContainer    = (CEGUI::DragContainer*)args.window;
      CEGUI::GridLayoutContainer* buttonGrid = ControllerUI::ActionButtons::Grid;
      CEGUI::Window* destWindow              = dragContainer->getCurrentDropTarget();
      ActionButtonList^ buttonModels         = OgreClient::Singleton->Data->ActionButtons;

      ControllerUI::ActionButtons::Window->setUsingAutoRenderingSurface(true);

      if (!destWindow)
         return true;

      // get index of source
      int index = (int)buttonGrid->getIdxOfChild(dragContainer);

      // dropped on rootwindow? unset button
      if (destWindow == ControllerUI::GUIRoot)
      {
         buttonModels[index]->SetToUnset();
      }
      else if (destWindow->getParent() == buttonGrid)
      {
         // find drop on other button
         int indexbtn = (int)buttonGrid->getIdxOfChild(destWindow);

         // swap datamodels
         buttonModels->Swap(index, indexbtn);

         // restore/swap labels
         CLRString^ temp = buttonModels[index]->Label;
         buttonModels[index]->Label = buttonModels[indexbtn]->Label;
         buttonModels[indexbtn]->Label = temp;
      }

      return true;
   };

   bool UICallbacks::ActionButtons::OnMouseDown(const CEGUI::EventArgs& e)
   {
      // set this window as moving one
      ControllerUI::MovingWindow = ControllerUI::ActionButtons::Window;

      return true;
   };

   bool UICallbacks::ActionButtons::OnMouseUp(const CEGUI::EventArgs& e)
   {
      // unset this window as moving one
      ControllerUI::MovingWindow = nullptr;

      return true;
   };
};};
