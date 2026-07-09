using UnityEngine;
using UnityEngine.EventSystems;

namespace Modpack.UI.Layout
{
	public class FlexibleDraggableObject : MonoBehaviour, IDragHandler, IEventSystemHandler
	{
		public GameObject Target;

		public void OnDrag(PointerEventData data)
		{
			this.Target.transform.Translate(data.delta);
		}
	}
}
