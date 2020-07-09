﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mealmate.Admin.Areas.Admin.ViewModels
{
    public class UserCreateViewModel
    {
        public string Name { get; set; }
        public string Username { get; set; }
        
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }

        public string Email { get; set; }
        public string Phone { get; set; }

        public List<RoleAssignListViewModel> Roles { get; set; }
        public UserCreateViewModel()
        {
            Roles = new List<RoleAssignListViewModel>();
        }
    }
}