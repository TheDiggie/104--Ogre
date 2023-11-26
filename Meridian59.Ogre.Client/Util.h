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
#include "OgreColourValue.h"
#include "OgreDataStream.h"
#include "OgreVector3.h"
#include "OgreVector2.h"
#include "OgrePixelFormat.h"
#include "OgreTextureManager.h"
#include "OgreHardwarePixelBuffer.h"
#include "OgreSceneNode.h"
#include "OgreSceneManager.h"
#include "OgreMaterialSerializer.h"
#include "CEGUI/CEGUI.h"
#include "CEGUI/RendererModules/Ogre/Renderer.h"
#pragma managed(pop)

#include "Constants.h"

namespace Meridian59 { namespace Ogre 
{
   using namespace ::Ogre;
   using namespace System::IO;
   using namespace System::Drawing;
   using namespace Meridian59::Common;
   using namespace Meridian59::Common::Enums;
   using namespace Meridian59::Common::Constants;
   using namespace Meridian59::Common::Interfaces;

   /// <summary>
   /// Basic converters and widespread used Ogre interactions.
   /// </summary>
   public ref class Util abstract sealed
   {
   public:
      #pragma region Vectors
      /// <summary>
      /// Creates an Ogre Vector3 from a V3 instance
      /// </summary>
      /// <param name="Vector"></param>
      /// <returns></returns>
      __forceinline static ::Ogre::Vector3 ToOgre(::Meridian59::Common::V3% Vector)
      {
         return ::Ogre::Vector3(Vector.X, Vector.Y, Vector.Z);
      };

      /// <summary>
      /// Creates a V3 from an Ogre Vector3
      /// </summary>
      /// <param name="Vector"></param>
      /// <returns></returns>
      __forceinline static ::Meridian59::Common::V3 ToV3(::Ogre::Vector3& Vector)
      {
         return ::Meridian59::Common::V3(Vector.x, Vector.y, Vector.z);
      };

      /// <summary>
      /// Creates a V3 from an Ogre Vector3
      /// </summary>
      /// <param name="Vector"></param>
      /// <returns></returns>
      __forceinline static ::Meridian59::Common::V3 ToV3(const ::Ogre::Vector3& Vector)
      {
         return ::Meridian59::Common::V3(Vector.x, Vector.y, Vector.z);
      };

      /// <summary>
      /// Creates an Ogre Vector3 from a V3 instance
      /// with Y, Z parts flipped.
      /// </summary>
      /// <param name="Vector"></param>
      /// <returns></returns>
      __forceinline static ::Ogre::Vector3 ToOgreYZFlipped(V3% Vector)
      {
         return ::Ogre::Vector3(Vector.X, Vector.Z, Vector.Y);
      };

      /// <summary>
      /// Creates a V3 from an Ogre Vector3 instance
      /// with Y, Z parts flipped.
      /// </summary>
      /// <param name="Vector"></param>
      /// <returns></returns>
      __forceinline static ::Meridian59::Common::V3 ToV3YZFlipped(::Ogre::Vector3& Vector)
      {
         return ::Meridian59::Common::V3(Vector.x, Vector.z, Vector.y);
      };

      /// <summary>
      /// Creates a V3 from an Ogre Vector3 instance
      /// with Y, Z parts flipped.
      /// </summary>
      /// <param name="Vector"></param>
      /// <returns></returns>
      __forceinline static ::Meridian59::Common::V3 ToV3YZFlipped(const ::Ogre::Vector3& Vector)
      {
         return ::Meridian59::Common::V3(Vector.x, Vector.z, Vector.y);
      };

      /// <summary>
      /// Creates an Ogre Vector2 from a V2 instance
      /// </summary>
      /// <param name="Vector"></param>
      /// <returns></returns>
      __forceinline static ::Ogre::Vector2 ToOgre(V2% Vector)
      {
         return ::Ogre::Vector2(Vector.X, Vector.Y);
      };

      /// <summary>
      /// Creates an Ogre Vector2 from a V2 instance with X,Y flipped
      /// </summary>
      /// <param name="Vector"></param>
      /// <returns></returns>
      __forceinline static ::Ogre::Vector2 ToOgreXYFlipped(V2% Vector)
      {
         return ::Ogre::Vector2(Vector.Y, Vector.X);
      };

      /// <summary>
      /// Creates a V2 instance from Ogre Vector2
      /// </summary>
      /// <param name="Vector"></param>
      /// <returns></returns>
      __forceinline static ::Meridian59::Common::V2 ToV2(::Ogre::Vector2* Vector)
      {
         return ::Meridian59::Common::V2(Vector->x, Vector->y);
      };
      #pragma endregion

      #pragma region Orientation
      /// <summary>
      /// Converts Ogre Orientation to a radian angle orientation.
      /// </summary>
      /// <param name="Orientation"></param>
      /// <returns></returns>
      __forceinline static float ToRadianAngle(Quaternion& Orientation)
      {
         // normalized direction of nodeorientation on ground plane           
         V2 direction;
         direction.X = Orientation.xAxis().z;
         direction.Y = -Orientation.xAxis().x;

         // return radian angle
         return (float)MathUtil::GetRadianForDirection(direction);     
      };

      /// <summary>
      /// Rotates the SceneNode to match an angle in radians.
      /// </summary>
      /// <param name="SceneNode"></param>
      /// <param name="RadianAngle"></param>
      __forceinline static void SetOrientationFromAngle(SceneNode& SceneNode, float RadianAngle)
      {
         // add quarter of full period offset
         RadianAngle += (float)GeometryConstants::QUARTERPERIOD;

         // get ogre radian from value
         const Radian rRadian(RadianAngle);

         // reset and yaw to given angle
         SceneNode.resetOrientation();
         SceneNode.yaw(-rRadian);
      };

      /// <summary>
      /// Returns euler angles for quaternion.
      /// </summary>
      /// <param name="Orientation"></param>
      __forceinline static bool ToEulerAngles(const Quaternion& Orientation, Radian& Yaw, Radian& Pitch, Radian& Roll)
      {
         // get rotation matrix
         Ogre::Matrix3 mat;
         Orientation.ToRotationMatrix(mat);

         // extract euler angles
         return mat.ToEulerAnglesYXZ(Yaw, Pitch, Roll);
      }
      #pragma endregion

      #pragma region Light		
      /// <summary>
      /// Converts a server ushort LightColor to an Ogre RGB color
      /// </summary>
      /// <param name="LightColor"></param>
      /// <returns></returns>
      __forceinline static ColourValue LightColorToOgreRGB(ushort LightColor)
      {
         // decode color from ushort (see d3drender.c for formulas)
         int r = ((LightColor >> 10) & 31) * 255 / 31;
         int g = ((LightColor >> 5) & 31) * 255 / 31;
         int b = (LightColor & 31) * 255 / 31;
            
         return ColourValue(r / 255.0f, g / 255.0f, b / 255.0f);
      };

      /// <summary>
      /// Converts a GrayScale value (0-255) to an Ogre RGB color
      /// </summary>
      /// <param name="Value"></param>
      /// <returns></returns>
      __forceinline static ColourValue LightIntensityToOgreRGB(unsigned char Value)
      {
         const float ratio = (float)Value / 255.0f;          
         return ColourValue(ratio, ratio, ratio);
      };

      /// <summary>
      /// Sets Ogre light parameters (color/attenuation) according to M59 info.
      /// </summary>
      /// <param name="Light"></param>
      /// <param name="LightOwner"></param>
      __forceinline static void UpdateFromILightOwner(Light& Light, ILightOwner^ LightOwner)
      {
         // decode color from ushort (see d3drender.c for formulas)                
         ColourValue baseColor = Util::LightColorToOgreRGB(LightOwner->LightingInfo->LightColor);

         // brigthness ratio (between 0.0 and 1.0)
         float ratio = (float)LightOwner->LightingInfo->LightIntensity / 255.0f;

         // set colors based on decoded color and intensity ratio
         Light.setDiffuseColour(baseColor);
         Light.setSpecularColour(baseColor);

         // light scaling by intensity
         float range = (LightOwner->LightingInfo->LightIntensity > 0) ?
               120.0f + 460.0f * ratio : 0.0f;

         if (LightOwner->LightingInfo->IsLightHighlight)
               range *= 0.12f;

         // only distance value is used in pixelshader
         Light.setAttenuation(range, 0.0f, 0.0f, 0.0f);
      };

      /// <summary>
      /// Possibly creates an Ogre Light from M59 light parameters,
      /// if light parameters indicate so.
      /// </summary>
      /// <param name="LightOwner">The object having the M59 light</param>
      /// <param name="SceneManager">SceneManager used for creation</param>
      /// <param name="LightName">Unique name of the light</param>
      /// <returns>Ogre Light instance or NULL</returns>
      __forceinline static ::Ogre::Light* CreateLight(
         ::Meridian59::Common::Interfaces::ILightOwner^ LightOwner, 
         ::Ogre::SceneManager* SceneManager, 
         ::Ogre::String& LightName)
      {
         // check if the m59 object is supposed to have a light or if it exists
         if (!LightOwner->LightingInfo->IsLightOn || SceneManager->hasLight(LightName))
            return nullptr;

         // create ogre light
         ::Ogre::Light* light = SceneManager->createLight(LightName);

         // set type to pointlight
         light->setType(Light::LightTypes::LT_POINT);

         // set color/attenuation
         UpdateFromILightOwner(*light, LightOwner);

         return light;
      };
      #pragma endregion

      #pragma region Textures & Materials

      /// <summary>
      /// Creates an Ogre texture from the Meridian 59 color palettes.
      /// This will be a A8R8G8B8 texture of size 256x256.
      /// It will store all the colors of one palette in one row.
      /// </summary>
      /// <param name="TextureName">Name of new Ogre texture</param>
      /// <param name="TextureGroup">ResourceGroup of new Ogre texture</param>
      __forceinline static void CreatePalettesTexture(::Ogre::String& TextureName, ::Ogre::String& TextureGroup)
      {
         TextureManager& texMan  = TextureManager::getSingleton();
         TexturePtr texPtr = texMan.getByName(TextureName, TextureGroup);

         // nothing to do if it exists already
         if (texPtr)
            return;

         // create texture
         texPtr = texMan.createManual(
            TextureName, TextureGroup,
            ::Ogre::TextureType::TEX_TYPE_2D,
            ::Meridian59::Drawing2D::ColorTransformation::COLORCOUNT, 
            ::Meridian59::Drawing2D::ColorTransformation::PALETTECOUNT, 
            0, ::Ogre::PixelFormat::PF_A8R8G8B8, ::Ogre::TU_DEFAULT, 0, false, 0);

         // lock
         unsigned int* texBuffer = (unsigned int*)texPtr->getBuffer()->lock(
            ::Ogre::HardwareBuffer::LockOptions::HBL_WRITE_ONLY);

         int k = 0;
         for(int i = 0; i < ::Meridian59::Drawing2D::ColorTransformation::PALETTECOUNT; i++)
         {
            for (int j = 0; j < ::Meridian59::Drawing2D::ColorTransformation::COLORCOUNT; j++)
            {
               texBuffer[k] = ::Meridian59::Drawing2D::ColorTransformation::Palettes[i][j];
               k++;
            }
         }

         // unlock
         texPtr->getBuffer()->unlock();
      };

      /// <summary>
      /// Creates A8 Ogre texture from 8-Bit indexed BgfBitmap.
      /// The alpha channel will store the palette index.
      /// So this is some kind of "native" 8-Bit texture.
      /// </summary>
      /// <param name="BgfBitmap">BgfBitmap instance (source)</param>
      /// <param name="TextureName">Name of new Ogre texture</param>
      /// <param name="TextureGroup">ResourceGroup of new Ogre texture</param>
      __forceinline static void CreateTextureA8(
         ::Meridian59::Files::BGF::BgfBitmap^ BgfBitmap, 
         const ::Ogre::String& TextureName, 
         const ::Ogre::String& TextureGroup)
      {
         if (!BgfBitmap || BgfBitmap->Width == 0 || BgfBitmap->Height == 0)
            return;

         TextureManager& texMan = TextureManager::getSingleton();
         TexturePtr texPtr = texMan.getByName(TextureName, TextureGroup);

         // if the texture exists, nothing to do
         if (texPtr)
            return;

         unsigned short width, height;
         HardwarePixelBufferSharedPtr pixPtr;

         width = (unsigned short)BgfBitmap->Width;
         height = (unsigned short)BgfBitmap->Height;

         // create manual (empty) texture
         texPtr = texMan.createManual(
            TextureName,
            TextureGroup,
            TextureType::TEX_TYPE_2D,
            width, height, 0,
            ::Ogre::PixelFormat::PF_A8,
            TU_DEFAULT, 0, false, 0);

         // get pixelbuffer
         pixPtr = texPtr->getBuffer();

         // lock it
         void* ptr = pixPtr->lock(HardwareBuffer::LockOptions::HBL_WRITE_ONLY);

         // fill the pixeldata to buffer
         BgfBitmap->FillPixelDataTo((::System::IntPtr)ptr, false, true);

         // unlock buffer
         pixPtr->unlock();

         // cleanup
         pixPtr.reset();
         texPtr.reset();
      };

      /// <summary>
      /// Creates A8R8G8B8 Ogre texture from 8-Bit indexed BgfBitmap.
      /// A fixed default palette with black transparency will be used.
      /// Will only do something, if there is no texture with that name yet.
      /// For creating images of objects see ImageComposers.
      /// </summary>
      /// <param name="BgfBitmap">BgfBitmap instance (source)</param>
      /// <param name="TextureName">Name of new Ogre texture</param>
      /// <param name="TextureGroup">ResourceGroup of new Ogre texture</param>
      __forceinline static void CreateTextureA8R8G8B8(
         ::Meridian59::Files::BGF::BgfBitmap^ BgfBitmap, 
         const ::Ogre::String& TextureName, 
         const ::Ogre::String& TextureGroup,
         int Mipmaps)
      {
         if (!BgfBitmap || BgfBitmap->Width == 0 || BgfBitmap->Height == 0)
            return;

         TextureManager& texMan = TextureManager::getSingleton();
         TexturePtr texPtr = texMan.getByName(TextureName, TextureGroup);

         // nothing to do if the texture exists
         if (texPtr)
            return;

         unsigned short width, height;
         HardwarePixelBufferSharedPtr pixPtr;

         width = (unsigned short)BgfBitmap->Width;
         height = (unsigned short)BgfBitmap->Height;

         // create manual (empty) texture
         texPtr = texMan.createManual(
            TextureName,
            TextureGroup,
            TextureType::TEX_TYPE_2D,
            width, height, Mipmaps,
            ::Ogre::PixelFormat::PF_A8R8G8B8,
            TU_DEFAULT, 0, false, 0);

         // get pixelbuffer
         pixPtr = texPtr->getBuffer();

         // lock it
         void* ptr = pixPtr->lock(HardwareBuffer::LockOptions::HBL_WRITE_ONLY);

         // fill the pixeldata to buffer
         BgfBitmap->FillPixelDataAsA8R8G8B8TransparencyBlack((unsigned int*)ptr, width);

         // unlock buffer
         pixPtr->unlock();

         // cleanup
         pixPtr.reset();
         texPtr.reset();
      };

      /// <summary>
      /// Creates A8R8G8B8 Ogre texture from A8R8G8B8 CLR Bitmap.
      /// Only if there is no texture with that name yet.
      /// </summary>
      /// <param name="Bitmap">CLR Bitmap instance</param>
      /// <param name="TextureName">Name of new Ogre texture</param>
      /// <param name="TextureGroup">ResourceGroup of new Ogre texture</param>
      __forceinline static void CreateTexture(
         ::System::Drawing::Bitmap^ Bitmap, 
         const ::Ogre::String& TextureName, 
         const ::Ogre::String& TextureGroup)
      {
         if (!Bitmap)
            return;

         TextureManager& texMan = TextureManager::getSingleton();

         // if exists, nothing to do
         if (texMan.resourceExists(TextureName))
            return;

         unsigned short width = (unsigned short)Bitmap->Width;
         unsigned short height = (unsigned short)Bitmap->Height;

         const System::Drawing::Rectangle rectangle = 
            System::Drawing::Rectangle(0, 0, Bitmap->Width, Bitmap->Height);

         // lock source bitmap to read pixeldata
         System::Drawing::Imaging::BitmapData^ bmpData = Bitmap->LockBits(
            rectangle,
            System::Drawing::Imaging::ImageLockMode::ReadOnly,
            System::Drawing::Imaging::PixelFormat::Format32bppArgb);

         // create manual (empty) texture
         TexturePtr texPtr = texMan.createManual(
            TextureName,
            TextureGroup,
            TextureType::TEX_TYPE_2D,
            width, height, MIP_DEFAULT,
            ::Ogre::PixelFormat::PF_A8R8G8B8,
            TU_DEFAULT, 0, false, 0);

         // get pointer to pixelbuf
         HardwarePixelBufferSharedPtr pixPtr = texPtr->getBuffer();

         PixelBox box = PixelBox(width, height, 1, ::Ogre::PixelFormat::PF_A8R8G8B8, (void*)bmpData->Scan0);

         // get pixels from souce bitmap
         pixPtr->blitFromMemory(box);

         // unlock source bitmap
         Bitmap->UnlockBits(bmpData);

         // cleanup
         pixPtr.reset();
         texPtr.reset();
      };

      /// <summary>
      /// Makes an OgreTexture available as image to CEGUI.
      /// </summary>
      /// <param name="OgreRenderer"></param>
      /// <param name="OgreTexture"></param>
      __forceinline static void CreateCEGUITextureFromOgre(CEGUI::OgreRenderer* OgreRenderer, TexturePtr OgreTexture)
      {
         if (!OgreTexture || OgreRenderer == nullptr)
            return;

         // get name of texture
         const ::Ogre::String& texName = OgreTexture->getName();

         // cegui image manager
         CEGUI::ImageManager& imgMan = CEGUI::ImageManager::getSingleton();

         // nothing to do, exists already in CEGUI
         if (OgreRenderer->isTextureDefined(texName) || imgMan.isDefined(texName))
            return;

         // create (wrap) CEGUI texture from Ogre texture
         CEGUI::Texture& mTexture = OgreRenderer->createTexture(texName, OgreTexture, true);

         // create CEGUI image for texture
         CEGUI::BasicImage& img = (CEGUI::BasicImage&)imgMan.create("BasicImage", texName);

         // apply texture to image and set size (because of subimaging)
         img.setTexture(&mTexture);
         img.setArea(CEGUI::Rectf(0.0f, 0.0f, (float)OgreTexture->getWidth(), (float)OgreTexture->getHeight()));
      };

      /// <summary>
      /// Clones the room base material to a new material and applies a texture.
      /// Only if there is no material with that name yet.
      /// </summary>
      /// <param name="MaterialName">Name of new material</param>
      /// <param name="TextureName">Name of texture to set on new material</param>
      /// <param name="MaterialGroup">ResourceGroup of new material</param>
      /// <param name="ScrollSpeed">NULL (default) or texture scrolling speed</param>
      /// <param name="ColorModifier">NULL (= 1 1 1 1) or a vector which components get multiplied with light components</param>
      __forceinline static void CreateMaterialRoom(
         const ::Ogre::String& MaterialName,
         const ::Ogre::String& TextureName,
         const ::Ogre::String& MaterialGroup,
         const ::Ogre::Vector2* ScrollSpeed)
      {
         MaterialManager& matMan = MaterialManager::getSingleton();

         // nothing to do, material exists already
         if (matMan.resourceExists(MaterialName))
            return;

         // try to get existing base material
         MaterialPtr baseMaterial = matMan.getByName(BASEMATERIALROOM, RESOURCEGROUPSHADER);

         // something wrong here, base material missing...
         if (!baseMaterial)
            return;

         // clone base material to different group
         MaterialPtr matPtr = baseMaterial->clone(MaterialName, true, MaterialGroup);

         // set the texture_unit part with name of the texture
         AliasTextureNamePairList pairs = AliasTextureNamePairList();
         pairs[TEXTUREUNITALIAS] = TextureName;

         // apply texture name
         matPtr->applyTextureAliases(pairs);

         // get shader pass
         Pass* pass = matPtr->getTechnique(0)->getPass(0);

         // get fragment shader parameters from ambient pass
         const GpuProgramParametersSharedPtr params = pass->getFragmentProgramParameters();

         // apply a scrolling if set
         if (ScrollSpeed != nullptr)
            pass->getTextureUnitState(0)->setScrollAnimation(ScrollSpeed->x, ScrollSpeed->y);

         // cleanup
         baseMaterial.reset();
         matPtr.reset();
      };

      /// <summary>
      /// Clones the base material for name labels to a new material and applies a texture.
      /// Only if there is no material with that name yet.
      /// </summary>
      /// <param name="MaterialName">Name of new material</param>
      /// <param name="TextureName">Name of texture to set on new material</param>
      /// <param name="MaterialGroup">ResourceGroup of new material</param>
      /// <param name="ColorModifier">NULL (= 1 1 1 1) or a vector which components get multiplied with light components</param>
      __forceinline static void CreateMaterialLabel(
         const ::Ogre::String& MaterialName,
         const ::Ogre::String& TextureName,
         const ::Ogre::String& MaterialGroup)
      {
         MaterialManager& matMan = MaterialManager::getSingleton();

         // nothing to do, material exists already
         if (matMan.resourceExists(MaterialName))
            return;

         // try to get existing base material
         MaterialPtr baseMaterial = matMan.getByName(BASEMATERIALLABEL, RESOURCEGROUPSHADER);

         // something wrong here, base material missing...
         if (!baseMaterial)
            return;

         // clone base material to different group
         MaterialPtr matPtr = baseMaterial->clone(MaterialName, true, MaterialGroup);

         // set the texture_unit part with name of the texture
         AliasTextureNamePairList pairs = AliasTextureNamePairList();
         pairs[TEXTUREUNITALIAS] = TextureName;

         // apply texture name
         matPtr->applyTextureAliases(pairs);

         // cleanup
         baseMaterial.reset();
         matPtr.reset();
      };

      /// <summary>
      /// Clones the base material for quest marker to a new material and applies a texture.
      /// Only if there is no material with that name yet.
      /// </summary>
      /// <param name="MaterialName">Name of new material</param>
      /// <param name="TextureName">Name of texture to set on new material</param>
      /// <param name="MaterialGroup">ResourceGroup of new material</param>
      /// <param name="ColorModifier">NULL (= 1 1 1 1) or a vector which components get multiplied with light components</param>
      __forceinline static void CreateMaterialQuestMarker(
         const ::Ogre::String& MaterialName,
         const ::Ogre::String& TextureName,
         const ::Ogre::String& MaterialGroup)
      {
         MaterialManager& matMan = MaterialManager::getSingleton();

         // nothing to do, material exists already
         if (matMan.resourceExists(MaterialName))
            return;

         // try to get existing base material
         MaterialPtr baseMaterial = matMan.getByName(BASEMATERIALQUESTMARKER, RESOURCEGROUPSHADER);

         // something wrong here, base material missing...
         if (!baseMaterial)
            return;

         // clone base material to different group
         MaterialPtr matPtr = baseMaterial->clone(MaterialName, true, MaterialGroup);

         // set the texture_unit part with name of the texture
         AliasTextureNamePairList pairs = AliasTextureNamePairList();
         pairs[TEXTUREUNITALIAS] = TextureName;

         // apply texture name
         matPtr->applyTextureAliases(pairs);

         // cleanup
         baseMaterial.reset();
         matPtr.reset();
      };

      /// <summary>
      /// Clones the base material to a new material and applies a texture.
      /// Only if there is no material with that name yet.
      /// </summary>
      /// <param name="BaseMaterialName">Base Material to clone</param>
      /// <param name="MaterialName">Name of new material</param>
      /// <param name="TextureName">Name of texture to set on new material</param>
      /// <param name="MaterialGroup">ResourceGroup of new material</param>
      /// <param name="ColorModifier">NULL (= 1 1 1 1) or a vector which components get multiplied with light components</param>
      __forceinline static void CreateMaterialObject(
         const ::Ogre::String& BaseMaterialName,
         const ::Ogre::String& MaterialName, 
         const ::Ogre::String& TextureName, 
         const ::Ogre::String& MaterialGroup, 
         const ::Ogre::Vector4* ColorModifier)
      {
         MaterialManager& matMan = MaterialManager::getSingleton();

         // nothing to do, material exists already
         if (matMan.resourceExists(MaterialName))
            return;

         // try to get existing base material
         MaterialPtr baseMaterial =  matMan.getByName(BaseMaterialName, RESOURCEGROUPSHADER);

         // something wrong here, base material missing...
         if (!baseMaterial)
            return;

         // clone base material to different group
         MaterialPtr matPtr = baseMaterial->clone(MaterialName, true, MaterialGroup);

         // set the texture_unit part with name of the texture
         AliasTextureNamePairList pairs = AliasTextureNamePairList();
         pairs[TEXTUREUNITALIAS] = TextureName;

         // apply texture name
         matPtr->applyTextureAliases(pairs);

         // get shader pass
         Pass* pass = matPtr->getTechnique(0)->getPass(0);
         
         // get fragment shader parameters from ambient pass
         const GpuProgramParametersSharedPtr params = pass->getFragmentProgramParameters();

         // apply a custom color modifier on the shaders
         // its components get multiplied with the color components
         if (ColorModifier != nullptr)
            params->setNamedConstant(SHADERCOLORMODIFIER, *ColorModifier);

         // cleanup
         baseMaterial.reset();
         matPtr.reset();
      };

      /// <summary>
      /// Clones the base water material to a new material and applies a texture.
      /// Only if there is no material with that name yet.
      /// </summary>
      /// <param name="MaterialName">Name of new material</param>
      /// <param name="TextureName">Name of texture to set on new material</param>
      /// <param name="MaterialGroup">ResourceGroup of new material</param>
      /// <param name="ScrollSpeed">NULL (default) or texture scrolling speed</param>
      __forceinline static void CreateMaterialWater(
         const ::Ogre::String& MaterialName,
         const ::Ogre::String& TextureName,
         const ::Ogre::String& MaterialGroup,
         ::Ogre::Vector2* ScrollSpeed)
      {
         MaterialManager& matMan = MaterialManager::getSingleton();

         // nothing to do if material exists
         if (matMan.resourceExists(MaterialName))
            return;

         // try to get existing base material
         MaterialPtr baseMaterial = matMan.getByName(BASEMATERIALWATER, RESOURCEGROUPSHADER);

         if (!baseMaterial)
            return;

         // clone base material to different group
         MaterialPtr matPtr = baseMaterial->clone(MaterialName, true, MaterialGroup);

         // set the texture_unit part with name of the texture
         AliasTextureNamePairList pairs = AliasTextureNamePairList();
         pairs[TEXTUREUNITALIAS] = TextureName;

         // apply texture name
         matPtr->applyTextureAliases(pairs);

         // get shader passes (0 = ambient, 1 = diffuse pointlights)
         Pass* ambientPass = matPtr->getTechnique(0)->getPass(0);
         //Pass* diffusePass = matPtr->getTechnique(0)->getPass(1);

         // get vertex shader parameters from ambient pass
         const GpuProgramParametersSharedPtr paramsAmbient =
            ambientPass->getVertexProgramParameters();

         // get fragment shader parameters from diffuse pass
         //const GpuProgramParametersSharedPtr paramsDiffuse =
         //	diffusePass->getFragmentProgramParameters();

         // set scrollspeed
         if (ScrollSpeed)
            paramsAmbient->setNamedConstant("waveSpeed", 0.3f * -(*ScrollSpeed));

         // cleanup
         baseMaterial.reset();
         matPtr.reset();
      };

      /// <summary>
      /// Clones the base invisible material to a new material and applies a texture.
      /// Only if there is no material with that name yet.
      /// </summary>
      /// <param name="MaterialName">Name of new material</param>
      /// <param name="TextureName">Name of texture to set on new material</param>
      /// <param name="MaterialGroup">ResourceGroup of new material</param>
      __forceinline static void CreateMaterialInvisible(
         const ::Ogre::String& MaterialName, 
         const ::Ogre::String& TextureName, 
         const ::Ogre::String& MaterialGroup)
      {
         MaterialManager& matMan = MaterialManager::getSingleton();

         // nothing to do if existant
         if (matMan.resourceExists(MaterialName))
            return;

         // try to get existing base material
         MaterialPtr baseMaterial = matMan.getByName(BASEMATERIALINVISIBLE, RESOURCEGROUPSHADER);

         if (!baseMaterial)
            return;

         // clone base material to different group
         MaterialPtr matPtr = baseMaterial->clone(MaterialName, true, MaterialGroup);

         // set the texture_unit part with name of the texture
         AliasTextureNamePairList pairs = AliasTextureNamePairList();
         pairs[TEXTUREUNITALIAS] = TextureName;

         // apply texture name
         matPtr->applyTextureAliases(pairs);

         // get shader passes (0 = ambient, 1 = diffuse pointlights)
         //Pass* ambientPass = matPtr->getTechnique(0)->getPass(0);

         // get fragment shader parameters from ambient pass
         //const GpuProgramParametersSharedPtr paramsAmbient = 
         //ambientPass->getFragmentProgramParameters();

         // set opaque values
         //paramsAmbient->setNamedConstant(SHADEROPAQUE, Opaque);

         // cleanup
         baseMaterial.reset();
         matPtr.reset();
      };
      #pragma endregion

      #pragma region Others	
      /// <summary>
      /// Returns a memorystream from Ogre DataStreamPtr
      /// </summary>
      /// <param name="dataPtr"></param>
      /// <returns></returns>
      __forceinline static ::System::IO::MemoryStream^ DataPtrToStream(::Ogre::DataStreamPtr dataPtr)
      {
         if (dataPtr->size() == 0)
            return nullptr;

         array<unsigned char>^ buffer = gcnew array<unsigned char>((int)dataPtr->size());
         pin_ptr<unsigned char> p = &buffer[0];

         //Read buffer.Length amount of data into bufferPtr
         dataPtr->read((void*)p, (uint)dataPtr->size());

         return gcnew MemoryStream(buffer);
      };

      /// <summary>
      /// Gets the boundingbox height of the biggest attached MovableObject
      /// </summary>
      /// <param name="Filename">Full path and filename to write</param>
      __forceinline static float GetSceneNodeHeight(SceneNode* SceneNode)
      {
         // put name above of node
         // since _getWorldAABB is not updated yet, we calculate the height			
         ::Ogre::SceneNode::ObjectIterator iterator = 
            SceneNode->getAttachedObjectIterator();

         float maxheight = 0.0f;
         float candidate;
         MovableObject* obj;

         while(iterator.hasMoreElements())
         {
            obj = iterator.getNext();
            candidate = (float)obj->getBoundingBox().getSize().y;

            if (candidate > maxheight)
               maxheight = candidate;
         }

         return maxheight;
      };

      static ::CEGUI::String GetChatString(ServerString^ ChatMessage, bool PlainMode)
      {
         if (PlainMode)
            return StringConvert::CLRToCEGUI(ChatMessage->FullString + ::System::Environment::NewLine);

         // text with CEGUI markup and escapes
         CLRString^ text = CLRString::Empty;

         // walk style definitions
         for each (ChatStyle^ style in ChatMessage->Styles)
         {
            // add fontselection
            // normal
            if (!style->IsBold && !style->IsCursive)
               text += UI_TAGFONT_NORMAL;

            // bold not italic
            else if (style->IsBold && !style->IsCursive)
               text += UI_TAGFONT_BOLD;

            // not bold but italic
            else if (!style->IsBold && style->IsCursive)
               text += UI_TAGFONT_ITALIC;

            // bold and italic
            else if (style->IsBold && style->IsCursive)
               text += UI_TAGFONT_BOLDITALIC;

            // add color
            switch (style->Color)
            {
            case ChatColor::Black: text += UI_TAGCOLOR_BLACK; break;
            case ChatColor::Blue: text += UI_TAGCOLOR_BLUE; break;
            case ChatColor::Green: text += UI_TAGCOLOR_CHATGREEN; break;
            case ChatColor::Purple: text += UI_TAGCOLOR_CHATPURPLE; break;
            case ChatColor::Red: text += UI_TAGCOLOR_CHATRED; break;
            case ChatColor::White: text += UI_TAGCOLOR_WHITE; break;
#ifndef VANILLA
            case ChatColor::Aquamarine: text += UI_TAGCOLOR_CHATAQUAMARINE; break;
            case ChatColor::Cyan: text += UI_TAGCOLOR_CHATCYAN; break;
            case ChatColor::Drab: text += UI_TAGCOLOR_CHATDRAB; break;
            case ChatColor::Emerald: text += UI_TAGCOLOR_CHATEMERALD; break;
            case ChatColor::Fire: text += UI_TAGCOLOR_CHATFIRE; break;
            case ChatColor::Champagne: text += UI_TAGCOLOR_CHATCHAMPAGNE; break;
            case ChatColor::ImperialBlue: text += UI_TAGCOLOR_CHATIMPERIALBLUE; break;
            case ChatColor::Jonquil: text += UI_TAGCOLOR_CHATJONQUIL; break;
            case ChatColor::Lime: text += UI_TAGCOLOR_CHATLIME; break;
            case ChatColor::Magenta: text += UI_TAGCOLOR_CHATMAGENTA; break;
            case ChatColor::Orange: text += UI_TAGCOLOR_CHATORANGE; break;
            case ChatColor::Pink: text += UI_TAGCOLOR_CHATPINK; break;
            case ChatColor::Steel: text += UI_TAGCOLOR_CHATSTEEL; break;
            case ChatColor::ToxicGreen: text += UI_TAGCOLOR_CHATTOXICGREEN; break;
            case ChatColor::OffWhite: text += UI_TAGCOLOR_CHATOFFWHITE; break;
            case ChatColor::Violet: text += UI_TAGCOLOR_CHATVIOLET; break;
            case ChatColor::Golden: text += UI_TAGCOLOR_CHATGOLDEN; break;
            case ChatColor::Yellow: text += UI_TAGCOLOR_CHATYELLOW; break;
            case ChatColor::Bronze: text += UI_TAGCOLOR_CHATBRONZE; break;
            case ChatColor::Gray1: text += UI_TAGCOLOR_CHATGRAY1; break;
            case ChatColor::Gray2: text += UI_TAGCOLOR_CHATGRAY2; break;
            case ChatColor::Gray3: text += UI_TAGCOLOR_CHATGRAY3; break;
            case ChatColor::Gray4: text += UI_TAGCOLOR_CHATGRAY4; break;
            case ChatColor::Gray5: text += UI_TAGCOLOR_CHATGRAY5; break;
            case ChatColor::Gray6: text += UI_TAGCOLOR_CHATGRAY6; break;
            case ChatColor::Gray7: text += UI_TAGCOLOR_CHATGRAY7; break;
            case ChatColor::Gray8: text += UI_TAGCOLOR_CHATGRAY8; break;
            case ChatColor::Gray9: text += UI_TAGCOLOR_CHATGRAY9; break;
            case ChatColor::Gray10: text += UI_TAGCOLOR_CHATGRAY10; break;
            case ChatColor::QuestGreen: text += UI_TAGCOLOR_CHATQUESTGREEN; break;
            case ChatColor::QuestRed: text += UI_TAGCOLOR_CHATQUESTRED; break;
            case ChatColor::MercenaryColor: text += UI_TAGCOLOR_CHATMERCENARY; break;
#endif
            }

            // get substring for this style
            CLRString^ str = ChatMessage->FullString->Substring(style->StartIndex, style->Length);

            // replace "\" with "\\"
            // and "[" with "\["
            str = str->Replace("\\", "\\\\");
            str = str->Replace("[", "\\[");

            // add textpart
            text += str;
         }

         // append line break
         text += ::System::Environment::NewLine;

         // return as CEGUI string
         return StringConvert::CLRToCEGUI(text);
      };
      #pragma endregion

      static bool GetScreenspaceCoords(::Ogre::MovableObject* object, ::Ogre::Camera* camera, ::Ogre::Vector2& result)
      {
         if(!object->isInScene())
            return false;
 
         const ::Ogre::AxisAlignedBox &AABB = object->getWorldBoundingBox(true);
 
         /**
         * If you need the point above the object instead of the center point:
         * This snippet derives the average point between the top-most corners of the bounding box
         * Ogre::Vector3 point = (AABB.getCorner(AxisAlignedBox::FAR_LEFT_TOP)
         *    + AABB.getCorner(AxisAlignedBox::FAR_RIGHT_TOP)
         *    + AABB.getCorner(AxisAlignedBox::NEAR_LEFT_TOP)
         *    + AABB.getCorner(AxisAlignedBox::NEAR_RIGHT_TOP)) / 4;
         */
 
         // Get the center point of the object's bounding box
         ::Ogre::Vector3 point = AABB.getCenter();
 
         // Is the camera facing that point? If not, return false
         ::Ogre::Plane cameraPlane = ::Ogre::Plane(::Ogre::Vector3(camera->getDerivedOrientation().zAxis()), camera->getDerivedPosition());
         if(cameraPlane.getSide(point) != ::Ogre::Plane::NEGATIVE_SIDE)
            return false;
 
         // Transform the 3D point into screen space
         point = camera->getProjectionMatrix() * (camera->getViewMatrix() * point);
 
         // Transform from coordinate space [-1, 1] to [0, 1] and update in-value
         result.x = (point.x / 2) + 0.5f;
         result.y = 1 - ((point.y / 2) + 0.5f);
 
         return true;
      };

      /// <summary>
      /// Exports all Materials to a set of .material files.
      /// </summary>
      /// <param name="Folder"></param>
      static void ExportMaterials(::Ogre::String Folder)
      {
         //::Ogre::MaterialSerializer matSerializerModels = ::Ogre::MaterialSerializer();
         /*::Ogre::MaterialSerializer matSerializerRoom = MaterialSerializer();
         ::Ogre::MaterialSerializer matSerializerRemoteNode2D = MaterialSerializer();
         ::Ogre::MaterialSerializer matSerializerMovableText = MaterialSerializer();
         ::Ogre::MaterialSerializer matSerializerProjectileNode = MaterialSerializer();*/

         /*::Ogre::ResourceManager::ResourceMapIterator iterator = 
         ::Ogre::MaterialManager::getSingletonPtr()->getResourceIterator();

         while (iterator.hasMoreElements())
         {
            ::Ogre::MaterialPtr matPtr = iterator.getNext().staticCast<::Ogre::Material>();
                
            if (matPtr->getGroup() == RESOURCEGROUPMODELS)
               matSerializerModels.queueForExport(matPtr);

            else if (matPtr->getGroup() == MATERIALGROUP_ROOLOADER)
               matSerializerRoom.queueForExport(matPtr);

            else if (matPtr->getGroup() == MATERIALGROUP_REMOTENODE2D)
               matSerializerRemoteNode2D.queueForExport(matPtr);

            else if (matPtr->getGroup() == MATERIALGROUP_MOVABLETEXT)
               matSerializerMovableText.queueForExport(matPtr);

            else if (matPtr->getGroup() == MATERIALGROUP_PROJECTILENODE2D)
               matSerializerProjectileNode.queueForExport(matPtr);
         }

         ::Ogre::String fileModels = ::Ogre::String(Folder);
         ::Ogre::String fileRoom = ::Ogre::String(Folder);
         ::Ogre::String fileRemoteNode2D = ::Ogre::String(Folder);
         ::Ogre::String fileMovableText = ::Ogre::String(Folder);
         ::Ogre::String fileProjectileNode = ::Ogre::String(Folder);

         fileModels.append("/models.material");
         fileRoom.append("/room.material");
         fileRemoteNode2D.append("/remotenode2d.material");
         fileMovableText.append("/movabletext.material");
         fileProjectileNode.append("/projectilenode.material");

         matSerializerModels.exportQueued(fileModels);
         matSerializerRoom.exportQueued(fileRoom);
         matSerializerRemoteNode2D.exportQueued(fileRemoteNode2D);
         matSerializerMovableText.exportQueued(fileMovableText);
         //matSerializerProjectileNode.exportQueued(fileProjectileNode);*/
      };
   };
};};