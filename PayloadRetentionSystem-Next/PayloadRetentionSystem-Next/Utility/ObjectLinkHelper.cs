using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PayloadRetentionSystemNext.Utility
{
	internal class ObjectLinkHelper
	{
		public static void GenerateId(Module.ModuleTrunnionPins part)
		{
			if(HighLogic.LoadedSceneIsEditor)
			{
				// if we want to link something in the editor, we need a unique id
				// we generate this here (this is not used when we are already in flight,
				// because there we can use the flightID of the part)
				// (info: the persistentId of the Part does change from time to time)

				do { part.partId = (uint)Guid.NewGuid().GetHashCode(); } while(part.partId == 0);

				for(int i = 0; i < EditorLogic.fetch.ship.parts.Count; i++)
				{
					Module.ModuleTrunnionPins _part = EditorLogic.fetch.ship.parts[i].GetComponent<Module.ModuleTrunnionPins>();

					if((_part != null) && (_part != part) && (_part.partId == part.partId))
					{
						do { part.partId = (uint)Guid.NewGuid().GetHashCode(); } while(part.partId == 0);
						i = 0;
					}
				}
			}
		}

		public static void Link(Module.ModuleTrunnionPins part, Module.ModuleTrunnionPins linkedPart)
		{
			GenerateId(part);
			GenerateId(linkedPart);

			part.companionPartId = linkedPart.partId;
			linkedPart.companionPartId = part.partId;
		}

		private static Module.ModuleTrunnionPins Find(Part part, uint partIdSearched)
		{
			Module.ModuleTrunnionPins _part = part.GetComponent<Module.ModuleTrunnionPins>();

			if(_part && (_part.partId == partIdSearched))
				return _part;

			foreach(Part c in part.children)
			{
				if(_part = Find(c, partIdSearched))
					return _part;
			}

			return null;
		}

		public static Module.ModuleTrunnionPins FindLinked(Module.ModuleTrunnionPins part)
		{
			if(HighLogic.LoadedSceneIsEditor)
			{
				Part r = part.part;
				while(r.parent) r = r.parent;

				return Find(r, part.companionPartId);
			}
			else
			{
				for(int i = 0; i < part.vessel.parts.Count; i++)
				{
					Module.ModuleTrunnionPins _part = part.vessel.parts[i].GetComponent<Module.ModuleTrunnionPins>();

					if((_part != null) && (_part.partId == part.companionPartId))
						return _part;
				}

				return null;
			}
		}
	}
}
