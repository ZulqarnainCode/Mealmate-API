﻿using AutoMapper;

using Mealmate.Api.Helpers;
using Mealmate.Api.Requests;
using Mealmate.Api.Services;
using Mealmate.Application.Interfaces;
using Mealmate.Application.Models;
using Mealmate.Core.Configuration;
using Mealmate.Core.Entities;
using Mealmate.Infrastructure.Data;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace Mealmate.Api.Controllers
{
    /// <summary>
    /// Account Controller
    /// </summary>
    [Route("api/accounts")]
    [ApiController]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class AccountController : ControllerBase
    {
        private readonly MealmateSettings _mealmateSettings;
        private readonly IRestaurantService _restaurantService;
        private readonly IUserRestaurantService _userRestaurantService;
        private readonly IEmailService _emailService;
        private readonly RoleManager<Role> _roleManager;
        private readonly IUserAllergenService _userAllergenService;
        private readonly IUserDietaryService _userDietaryService;
        private readonly IFacebookAuthService _facebookAuthService;
        private readonly IGoogleAuthService _googleAuthService;
        private readonly SignInManager<User> _signInManager;
        private readonly UserManager<User> _userManager;
        private readonly IMapper _mapper;
        private readonly TokenValidationParameters _tokenValidationParameters;
        private readonly MealmateContext _mealmateContext;

        public AccountController(SignInManager<User> signInManager,
          UserManager<User> userManager,
          RoleManager<Role> roleManager,
          IOptions<MealmateSettings> options,
          IMapper mapper,
          IRestaurantService restaurantService,
          IUserRestaurantService userRestaurantService,
          IEmailService emailService,
          IUserAllergenService userAllergenService,
          IUserDietaryService userDietaryService,
          IFacebookAuthService facebookAuthService,
          IGoogleAuthService googleAuthService,
          TokenValidationParameters tokenValidationParameters,
          MealmateContext mealmateContext)
        {
            _mapper = mapper;
            _signInManager = signInManager;
            _userManager = userManager;
            _mealmateSettings = options.Value;
            _restaurantService = restaurantService;
            _userRestaurantService = userRestaurantService;
            _emailService = emailService;
            _roleManager = roleManager;
            _userAllergenService = userAllergenService;
            _userDietaryService = userDietaryService;
            _facebookAuthService = facebookAuthService;
            _googleAuthService = googleAuthService;
            _tokenValidationParameters = tokenValidationParameters;
            _mealmateContext = mealmateContext;
        }

        #region Create JWT
        private async Task<AuthenticationResult> GenerateJwtToken(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(JwtRegisteredClaimNames.Sub, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("id", user.Id.ToString())
            };

            var roles = await _userManager.GetRolesAsync(user);

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_mealmateSettings.Tokens.Key));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddDays(1),
                SigningCredentials = creds,
                Issuer = _mealmateSettings.Tokens.Issuer,
                Audience = _mealmateSettings.Tokens.Audience
            };

            var tokenHandler = new JwtSecurityTokenHandler();

            var token = tokenHandler.CreateToken(tokenDescriptor);
            var refreshToken = new RefreshToken
            {
                JwtId = token.Id,
                UserId = user.Id,
                CreationDate = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddMonths(6)
            };

            await _mealmateContext.RefreshTokens.AddAsync(refreshToken);
            await _mealmateContext.SaveChangesAsync();

            return new AuthenticationResult
            {
                Success = true,
                Token = tokenHandler.WriteToken(token),
                RefreshToken = refreshToken.Id
            };

        }
        private bool IsJwtWithValidSecurityAlgorithm(SecurityToken validatedToken)
        {
            return (validatedToken is JwtSecurityToken jwtSecurityToken) &&
                   jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha512, StringComparison.InvariantCultureIgnoreCase);
        }
        #endregion
        private ClaimsPrincipal GetPrincipalFromToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();

            try
            {
                var tokenValidationParameters = _tokenValidationParameters.Clone();
                tokenValidationParameters.ValidateLifetime = false;
                var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var validatedToken);
                if (!IsJwtWithValidSecurityAlgorithm(validatedToken))
                {
                    return null;
                }

                return principal;
            }
            catch
            {
                return null;
            }
        }



        #region Login
        /// <summary>
        /// Login
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user != null)
            {
                var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);

                if (result.Succeeded)
                {
                    var appUser = await _userManager.Users
                                                    .FirstOrDefaultAsync(
                                           u => u.Email.ToUpper() == request.Email.ToUpper());

                    var userToReturn = _mapper.Map<UserModel>(appUser);
                    var authResponse = await GenerateJwtToken(appUser);
                    return Ok(new
                    {
                        token = authResponse.Token,
                        refreshToken = authResponse.RefreshToken,
                        user = userToReturn,
                    });
                }
                else
                {
                    return Unauthorized("UserName of Password is incorrect");
                }

            }
            return Unauthorized("UserName of Password is incorrect");

        }

        [AllowAnonymous]
        [HttpPost("signin-facebook")]
        public async Task<IActionResult> LoginWithFacebook([FromBody] FacebookLoginRequest request)
        {
            var validatedTokenResult = await _facebookAuthService.ValidateAccessTokenAsync(request.AccessToken);

            if (!validatedTokenResult.Data.IsValid)
            {
                return BadRequest("your access token is invalid");
            }

            var userInfo = await _facebookAuthService.GetUserInfoAsync(request.AccessToken);

            var user = await _userManager.FindByEmailAsync(userInfo.Email);

            if (user == null)
            {
                var newUser = new User
                {
                    Email = userInfo.Email,
                    UserName = userInfo.Email,
                    FirstName = userInfo.FirstName,
                    LastName = userInfo.LastName
                };

                var createdResult = await _userManager.CreateAsync(newUser);
                if (!createdResult.Succeeded)
                {
                    return BadRequest("something went wrong");
                }

                var authResponse1 = await GenerateJwtToken(newUser);
                var newUserToReturn = _mapper.Map<UserModel>(newUser);

                return Ok(new
                {
                    token = authResponse1.Token,
                    refreshToken = authResponse1.RefreshToken,
                    user = newUserToReturn
                });

            }
            var authResponse = await GenerateJwtToken(user);
            var userToReturn = _mapper.Map<UserModel>(user);
            return Ok(new
            {
                token = authResponse.Token,
                refreshToken = authResponse.RefreshToken,
                user = userToReturn
            });
        }



        [AllowAnonymous]
        [HttpPost("signin-google")]
        public async Task<IActionResult> LoginWithGoogle([FromBody] GoogleLoginRequest request)
        {
            var validatedTokenResult = await _googleAuthService.ValidateAccessTokenAsync(request.AccessToken);

            if (validatedTokenResult == null)
            {
                return BadRequest("your access token is invalid");
            }

            var userInfo = await _googleAuthService.GetUserInfoAsync(request.IdToken);

            var user = await _userManager.FindByEmailAsync(userInfo.Email);

            if (user == null)
            {
                var newUser = new User
                {
                    Email = userInfo.Email,
                    UserName = userInfo.Email,
                    FirstName = userInfo.Name,
                    LastName = userInfo.FamilyName
                };

                var createdResult = await _userManager.CreateAsync(newUser);
                if (!createdResult.Succeeded)
                {
                    return BadRequest("something went wrong");
                }
                var authResponse1 = await GenerateJwtToken(newUser);
                var newUserToReturn = _mapper.Map<UserModel>(newUser);

                return Ok(new
                {
                    token = authResponse1.Token,
                    refreshToken = authResponse1.RefreshToken,
                    user = newUserToReturn
                });

            }
            var authResponse = await GenerateJwtToken(user);
            var userToReturn = _mapper.Map<UserModel>(user);
            return Ok(new
            {
                token = authResponse.Token,
                refreshToken = authResponse.RefreshToken,
                user = userToReturn
            });
        }

        #endregion

        #region Refresh Token
        [AllowAnonymous]
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
        {
            var validatedToken = GetPrincipalFromToken(request.Token);

            if (validatedToken == null)
            {
                return BadRequest(new[] { "Invalid Token" });
            }

            var expiryDateUnix =
                long.Parse(validatedToken.Claims.Single(x => x.Type == JwtRegisteredClaimNames.Exp).Value);

            var expiryDateTimeUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddSeconds(expiryDateUnix);

            if (expiryDateTimeUtc > DateTime.UtcNow)
            {
                return BadRequest(new[] { "This token hasn't expired yet" });
            }

            var jti = validatedToken.Claims.Single(x => x.Type == JwtRegisteredClaimNames.Jti).Value;

            var storedRefreshToken = await _mealmateContext.RefreshTokens.SingleOrDefaultAsync(x => x.Id == request.RefreshToken);

            if (storedRefreshToken == null)
            {
                return BadRequest(new[] { "This refresh token does not exist" });
            }

            if (DateTime.UtcNow > storedRefreshToken.ExpiryDate)
            {
                return BadRequest(new[] { "This refresh token has expired" });
            }

            if (storedRefreshToken.Invalidated)
            {
                return BadRequest(new[] { "This refresh token has been invalidated" });
            }

            if (storedRefreshToken.Used)
            {
                return BadRequest(new[] { "This refresh token has been used" });
            }

            if (storedRefreshToken.JwtId != jti)
            {
                return BadRequest(new[] { "This refresh token does not match this JWT" });
            }

            storedRefreshToken.Used = true;
            _mealmateContext.RefreshTokens.Update(storedRefreshToken);
            await _mealmateContext.SaveChangesAsync();

            var user = await _userManager.FindByIdAsync(validatedToken.Claims.Single(x => x.Type == "id").Value);
            var authResponse = await GenerateJwtToken(user);

            return Ok(new
            {
                token = authResponse.Token,
                refreshToken = authResponse.RefreshToken
            });
        }
        #endregion

        #region Change Password
        [HttpPost("changepassword")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var user = await _userManager.FindByNameAsync(request.UserName);
            var result = await _signInManager.CheckPasswordSignInAsync(user, request.OldPassword, false);
            if (result.Succeeded)
            {
                var change = await _userManager.ChangePasswordAsync(user, request.OldPassword, request.NewPassword);
                if (change.Succeeded)
                    return Ok();
            }

            return Unauthorized();
        }
        #endregion

        #region Sign Out
        [HttpPost("logout")]
        public async Task<IActionResult> SignOut(string userName)
        {
            var user = await _userManager.FindByNameAsync(userName);
            if (user != null)
            {
                await _signInManager.SignOutAsync();
                return Ok();
            }

            return Unauthorized();
        }
        #endregion

        #region Register
        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<ActionResult> Register([FromBody] RegisterRequest model)
        {
            //TODO: Add you code here

            if (ModelState.IsValid)
            {
                var user = new User
                {
                    UserName = model.Email,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    Email = model.Email,
                    PhoneNumber = model.PhoneNumber
                };

                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    bool roleExists = await _roleManager.RoleExistsAsync(ApplicationRoles.RestaurantAdmin);
                    if (!roleExists)
                    {
                        //Create Role
                        await _roleManager.CreateAsync(new Role(ApplicationRoles.RestaurantAdmin));
                    }
                    var userIsInRole = await _userManager.IsInRoleAsync(user, ApplicationRoles.RestaurantAdmin);
                    if (!userIsInRole)
                    {
                        await _userManager.AddToRoleAsync(user, ApplicationRoles.RestaurantAdmin);
                    }

                    if (model.IsRestaurantAdmin)
                    {
                        var restaurant = new RestaurantCreateModel
                        {
                            Name = model.RestaurantName,
                            Description = model.RestaurantDescription,
                            IsActive = true
                        };

                        var data = await _restaurantService.Create(restaurant);

                        if (data != null)
                        {

                            var userRestaurant = new UserRestaurantCreateModel
                            {
                                UserId = user.Id,
                                RestaurantId = data.Id,
                                IsActive = true,
                                IsOwner = true

                            };

                            await _userRestaurantService.Create(userRestaurant);

                            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

                            string siteURL = _mealmateSettings.ClientAppUrl;
                            var callbackUrl = string.Format("{0}/Account/ConfirmEmail?userId={1}&code={2}", siteURL, user.Id, token);
                            //var callbackUrl = Url.Action("ConfirmEmail", "Account", new { userId = user.Id, code = code }, protocol: HttpContext.Request.Scheme);
                            var message = $"Please confirm your account by clicking this link: <a href='{callbackUrl}'>link</a>";
                            await _emailService.SendEmailAsync(model.Email, "Confirm your account", message);
                        }
                    }

                    var restaurants = await _restaurantService.Get(user.Id);

                    var owner = _mapper.Map<UserModel>(user);
                    owner.Restaurants = restaurants;

                    return Created($"/api/users/{user.Id}", owner);
                }
                else
                {
                    return BadRequest($"Error registering new user");
                }
            }
            return BadRequest(ModelState);
        }
        #endregion

        #region Register Mobile
        [AllowAnonymous]
        [HttpPost("mobileregister")]
        public async Task<ActionResult> MobileRegister([FromBody] MobileRegisterRequest model)
        {
            //TODO: Add you code here

            if (ModelState.IsValid)
            {
                var user = new User
                {
                    UserName = model.Email,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    Email = model.Email,
                    PhoneNumber = model.PhoneNumber
                };

                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    bool roleExists = await _roleManager.RoleExistsAsync(ApplicationRoles.Client);
                    if (!roleExists)
                    {
                        //Create Role
                        await _roleManager.CreateAsync(new Role(ApplicationRoles.Client));
                    }
                    var userIsInRole = await _userManager.IsInRoleAsync(user, ApplicationRoles.Client);
                    if (!userIsInRole)
                    {
                        await _userManager.AddToRoleAsync(user, ApplicationRoles.Client);
                    }

                    var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    string siteURL = _mealmateSettings.ClientAppUrl;
                    var callbackUrl = string.Format("{0}/Account/ConfirmEmail?userId={1}&code={2}", siteURL, user.Id, token);
                    //var callbackUrl = Url.Action("ConfirmEmail", "Account", new { userId = user.Id, code = code }, protocol: HttpContext.Request.Scheme);
                    var message = $"Please confirm your account by clicking this link: <a href='{callbackUrl}'>link</a>";
                    await _emailService.SendEmailAsync(model.Email, "Confirm your account", message);


                    if (model.UserAllergens.Count > 0)
                    {
                        foreach (var userAllergen in model.UserAllergens)
                        {
                            userAllergen.UserId = user.Id;
                            await _userAllergenService.Create(userAllergen);
                        }
                    }

                    if (model.UserDietaries.Count > 0)
                    {
                        foreach (var userDietary in model.UserDietaries)
                        {
                            userDietary.UserId = user.Id;
                            await _userDietaryService.Create(userDietary);
                        }
                    }
                    var owner = _mapper.Map<UserModel>(user);

                    return Created($"/api/users/{user.Id}", owner);
                }
                else
                {
                    return BadRequest($"Error registering new user");
                }
            }
            return BadRequest(ModelState);
        }
        #endregion

        #region Reset Password
        [AllowAnonymous]
        [HttpGet("resetpassword")]
        public async Task<ActionResult> ResetPassword(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user != null)
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var callbackUrl = Url.Action("ConfirmEmail", "Account", new { userId = user.Id, token }, protocol: HttpContext.Request.Scheme);
                var message = $"Please confirm your account by clicking this link: <a href='{callbackUrl}'>link</a>";
                await _emailService.SendEmailAsync(email, "Reset Password", message);
                return Ok("Check Your email...");
            }
            return BadRequest();
        }

        [AllowAnonymous]
        [HttpPost("resetpassword")]
        public async Task<ActionResult> ChangePassword([FromBody] ResetPasswordRequest request)
        {
            var user = await _userManager.FindByNameAsync(request.UserName);
            if (user != null)
            {
                var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
                if (result.Succeeded)
                {
                    return Ok("User Password Changed Successfully!");
                }
            }
            return BadRequest();
        }

        #endregion

        #region OTP
        [HttpPost()]
        public ActionResult SendOTP()
        {
            return Ok();
        }
        #endregion

    }
}
