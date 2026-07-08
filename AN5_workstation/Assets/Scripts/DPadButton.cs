using UnityEngine;
        using UnityEngine.EventSystems;
        
        public class DPadButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
        {
            public bool isPressed { get; private set; }
            public System.Action onFirstPress;
            bool _everPressed;
        
            public void OnPointerDown(PointerEventData e)
            {
                // Fire onFirstPress only once, before setting isPressed
                if (!_everPressed)
                {
                    _everPressed = true;
                    onFirstPress?.Invoke();
                }
                isPressed = true;
            }
        
            public void OnPointerUp(PointerEventData e)   => isPressed = false;
            public void OnPointerExit(PointerEventData e)  => isPressed = false;
        }
        