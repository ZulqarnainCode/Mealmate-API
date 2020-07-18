using Mealmate.Application.Models.Base;

using System;
using System.Collections.Generic;

namespace Mealmate.Application.Models
{
    public class OrderItemModel : BaseModel
    {
        public int MenuItemId { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public int OrderId { get; set; }
        public DateTimeOffset Created { get; set; }

        public ICollection<OrderItemDetailModel> OrderItemDetails { get; set; }
        public OrderItemModel()
        {
            OrderItemDetails = new HashSet<OrderItemDetailModel>();
        }
    }
}