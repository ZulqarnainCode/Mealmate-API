using System;
using System.Collections.Generic;
using Mealmate.Core.Entities.Base;

namespace Mealmate.Core.Entities
{
    public class MenuItem : Entity
    {
        //public int MenuItemId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public byte[] Photo { get; set; }
        public decimal Price { get; set; }
        public DateTimeOffset Created { get; set; }

        public int MenuId { get; set; }
        public virtual Menu Menu { get; set; }

        public virtual ICollection<MenuItemOption> MenuItemOptions { get; set; }

        public MenuItem()
        {
            MenuItemOptions = new HashSet<MenuItemOption>();
        }
    }
}