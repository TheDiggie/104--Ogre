/*
 Copyright (c) 2012-2013 Clint Banzhaf
 This file is part of "Meridian59 .NET".

 "Meridian59 .NET" is free software: 
 You can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, 
 either version 3 of the License, or (at your option) any later version.

 "Meridian59 .NET" is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
 without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
 See the GNU General Public License for more details.

 You should have received a copy of the GNU General Public License along with "Meridian59 .NET".
 If not, see http://www.gnu.org/licenses/.
*/

#pragma once

#pragma managed(push, off)
#include "OgreSceneNode.h"
#include "OgreEntity.h"
#include "ParticleUniverseSystem.h"
#pragma managed(pop)

#include "RemoteNode.h"
#include "Model3DInfo.h"
#include "MeshInfo.h"

namespace Meridian59 { namespace Ogre 
{
   using namespace ::Ogre;
   using namespace System::ComponentModel;
   using namespace System::Collections::Generic;
   using namespace Meridian59::Common::Constants;
   using namespace Meridian59::Data::Models;

   ref class RemoteNode3D;

   public ref class RemoteNode3DSub
   {
   protected:
      ::Ogre::Entity*		entity;
      ::Ogre::SceneNode*	sceneNode;
      ::Ogre::SceneNode*	parent;
      std::vector<::ParticleUniverse::ParticleSystem*>* particleSystems;

      Model3DInfo^	model3DInfo;
      SubOverlay^		subOverlay;
      RemoteNode3D^	rootNode;
      MeshHotspot^	attachedHotspot;
      ::System::Collections::Generic::List<RemoteNode3DSub^>^ subNodes;

      void CreateMesh();
      void CreateSubNodes();
      void CreateParticles();

   public:

      #pragma region Properties
      property ::Ogre::Entity* Entity 
      { 
         public: ::Ogre::Entity* get() { return entity; } 
         protected: void set(::Ogre::Entity* value) { entity = value; }
      }
        
      property ::Ogre::SceneNode* SceneNode 
      { 
         public: ::Ogre::SceneNode* get() { return sceneNode; } 
         protected: void set(::Ogre::SceneNode* value) { sceneNode = value; } 
      }
        
      property std::vector<::ParticleUniverse::ParticleSystem*>* ParticleSystems 
      { 
         public: std::vector<::ParticleUniverse::ParticleSystem*>* get() { return particleSystems; } 
         protected: void set(std::vector<::ParticleUniverse::ParticleSystem*>* value) { particleSystems = value; } 
      }

         property ::Meridian59::Ogre::Model3DInfo^ Model3DInfo 
      { 
         public: ::Meridian59::Ogre::Model3DInfo^ get() { return model3DInfo; }
         protected: void set(::Meridian59::Ogre::Model3DInfo^ value) { model3DInfo = value; }
      }
        
      property SubOverlay^ SubOverlay 
      { 
         public: Data::Models::SubOverlay^ get() { return subOverlay; }
         protected: void set(Data::Models::SubOverlay^ value) { subOverlay = value; }
      }

         property RemoteNode3D^ RootNode 
      { 
         public: RemoteNode3D^ get() { return rootNode; } 
         protected: void set(RemoteNode3D^ value) { rootNode = value; }
      }
        
      property ::Ogre::SceneNode* Parent 
      { 
         public: ::Ogre::SceneNode* get() { return parent; }
         protected: void set(::Ogre::SceneNode* value) { parent = value; }
      }
        
      property MeshHotspot^ AttachedHotspot 
      { 
         public: MeshHotspot^ get() { return attachedHotspot; } 
         protected: void set(MeshHotspot^ value) { attachedHotspot = value; }
      }
        
      property ::System::Collections::Generic::List<RemoteNode3DSub^>^ SubNodes 
      { 
         public: ::System::Collections::Generic::List<RemoteNode3DSub^>^ get() { return subNodes; }
         protected: void set(::System::Collections::Generic::List<RemoteNode3DSub^>^ value) { subNodes = value; }
      }
        
      property CLRString^ ID 
      { 
         CLRString^ get(); 
      }
      #pragma endregion

      RemoteNode3DSub(Data::Models::SubOverlay^ SubOverlay, ::Ogre::SceneNode* Parent, RemoteNode3D^ RootNode, MeshHotspot^ AttachedHotspot);

      void Destroy();
   };
};};

