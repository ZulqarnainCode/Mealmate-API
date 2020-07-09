﻿using Mealmate.DataAccess.Entities.Identity;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace Mealmate.DataAccess.Entities.Mealmate
{
    public class Restaurant
    {
        public int RestaurantId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTimeOffset Created { get; set; }

        public int OwnerId { get; set; }
        public virtual User Owner { get; set; }

        public virtual ICollection<Branch> Branches { get; set; }

        public Restaurant()
        {
            Branches = new HashSet<Branch>();
        }
    }
}