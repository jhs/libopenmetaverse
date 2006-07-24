using System;

using libsecondlife;
using libsecondlife.AssetSystem;

namespace libsecondlife.InventorySystem
{
	/// <summary>
	/// Summary description for InventoryNotecard.
	/// </summary>
	public class InventoryNotecard : InventoryItem
	{
		public string Body
		{
			get
			{
				if( _Asset != null )
				{
					return ((NotecardAsset)_Asset).Body;
				} else {
					if( (AssetID != null) && (AssetID != new LLUUID()) )
					{
						base.iManager.AssetManager.GetInventoryAsset( this );
						return ((NotecardAsset)_Asset).Body;
					}
				}

				return null;
			}

			set
			{
				base._Asset = new NotecardAsset( LLUUID.GenerateUUID(), value );
				base.iManager.AssetManager.UploadAsset( _Asset );
				this.AssetID = _Asset.AssetID;
				
			}
		}


		internal InventoryNotecard( InventoryManager manager, string name, string description, LLUUID id, LLUUID folderID, LLUUID uuidOwnerCreater ) 
			: base(manager, name, description, id, folderID, 7, 7, uuidOwnerCreater)
		{

		}

		internal InventoryNotecard( InventoryManager manager, InventoryItem ii )
			: base( manager, ii._Name, ii._Description, ii._ItemID, ii._FolderID, ii._InvType, ii._Type, ii._CreatorID)
		{
			if( (ii.InvType != 7) || (ii.Type != Asset.ASSET_TYPE_NOTECARD) )
			{
				throw new Exception("The InventoryItem cannot be converted to a Notecard, wrong InvType/Type.");
			}

			this.iManager = manager;
			this._Asset = ii._Asset;
			this._AssetID = ii._AssetID;
			this._BaseMask = ii._BaseMask;
			this._CRC = ii._CRC;
			this._CreationDate = ii._CreationDate;
			this._EveryoneMask = ii._EveryoneMask;
			this._Flags = ii._Flags;
			this._GroupID = ii._GroupID;
			this._GroupMask = ii._GroupMask;
			this._GroupOwned = ii._GroupOwned;
			this._InvType = ii._InvType;
			this._NextOwnerMask = ii._NextOwnerMask;
			this._OwnerID = ii._OwnerID;
			this._OwnerMask = ii._OwnerMask;
			this._SalePrice = ii._SalePrice;
			this._SaleType = ii._SaleType;
			this._Type = ii._Type;
		}

		public void ClearAssetTest()
		{
			_Asset = null;
		}

		override internal void SetAssetData( byte[] assetData )
		{
			if( _Asset == null )
			{
				if( AssetID != null )
				{
					_Asset = new NotecardAsset( AssetID, assetData );
				} 
				else 
				{
					_Asset = new NotecardAsset( LLUUID.GenerateUUID(), assetData );
					AssetID = _Asset.AssetID;
				}
			} 
			else 
			{
				_Asset.AssetData = assetData;
			}

		}

		override public string toXML()
		{
			string output = "<notecard ";

			output += "name = '" + xmlSafe(Name) + "' ";
			output += "uuid = '" + ItemID + "' ";
			output += "invtype = '" + InvType + "' ";
			output += "type = '" + Type + "' ";



			output += "description = '" + xmlSafe(Description) + "' ";
			output += "crc = '" + CRC + "' ";
			output += "debug = '" + PacketHelpers.UpdateInventoryItem.BuildCRC(this) + "' ";
			output += "ownerid = '" + OwnerID + "' ";
			output += "creatorid = '" + CreatorID + "' ";

			output += "assetid = '" + AssetID + "' ";
			output += "groupid = '" + GroupID + "' ";

			output += "groupowned = '" + GroupOwned + "' ";
			output += "creationdate = '" + CreationDate + "' ";
			output += "flags = '" + Flags + "' ";

			output += "saletype = '" + SaleType + "' ";
			output += "saleprice = '" + SalePrice + "' ";
			output += "basemask = '" + BaseMask + "' ";
			output += "everyonemask = '" + EveryoneMask + "' ";
			output += "nextownermask = '" + NextOwnerMask + "' ";
			output += "groupmask = '" + GroupMask + "' ";
			output += "ownermask = '" + OwnerMask + "' ";

			output += ">";

			output += Body;
			output += "</notecard>";


			return output;
		}

	}
}