using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Spice.Data;
using Spice.Models;
using Spice.Models.ViewModels;
using Spice.Utility;
using Stripe;

namespace Spice.Areas.Customer.Controllers
{
    [Area("Customer")]
    public class CartController : Controller
    {

        private readonly ApplicationDbContext _db;
        private readonly IEmailSender _emailSender;

        public CartController(ApplicationDbContext db,IEmailSender emailSender)
        {
            _db = db;
            _emailSender = emailSender;
        }

        [BindProperty]
        public OrderDetailsCart detailsCart { get; set; }

        public async Task<IActionResult> Index()
        {
            detailsCart = new OrderDetailsCart()
            {
                OrderHeader=new Models.OrderHeader()
            };
            detailsCart.OrderHeader.OrderTotal = 0;

            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);

            var cart = _db.ShoppingCart.Where(c => c.ApplicationUserId == claim.Value);

            if(cart!=null)
            {
                detailsCart.listCart = cart.ToList();
            }

            foreach(var list in detailsCart.listCart)
            {
                list.MenuItem = await _db.MenuItem.FirstOrDefaultAsync(m => m.Id == list.MenuItemId);
                detailsCart.OrderHeader.OrderTotal += (list.MenuItem.Price * list.Count);
                list.MenuItem.Description = SD.ConvertToRawHtml(list.MenuItem.Description);
                if (list.MenuItem.Description.Length > 100)
                {
                    list.MenuItem.Description = list.MenuItem.Description.Substring(0, 99) + "...";
                }
            }
            detailsCart.OrderHeader.OrderTotalOriginal = detailsCart.OrderHeader.OrderTotal;
            if (HttpContext.Session.GetString(SD.ssCouponCode) != null)
            {
                detailsCart.OrderHeader.CouponCode = HttpContext.Session.GetString(SD.ssCouponCode);
                var couponFromDb = await _db.Coupon.Where(c => c.Name.ToLower() == detailsCart.OrderHeader.CouponCode.ToLower()).FirstOrDefaultAsync ();

                detailsCart.OrderHeader.OrderTotal = SD.DiscountedPrice(couponFromDb, detailsCart.OrderHeader.OrderTotalOriginal);
            }
            return View(detailsCart);
        }

        public IActionResult AddCoupon()
        {
            if (detailsCart.OrderHeader.CouponCode == null)
            {
                detailsCart.OrderHeader.CouponCode = "";
            }
            HttpContext.Session.SetString(SD.ssCouponCode, detailsCart.OrderHeader.CouponCode);
            return RedirectToAction(nameof(Index));
        }
        public IActionResult RemoveCoupon()
        {
           
            HttpContext.Session.SetString(SD.ssCouponCode, string.Empty);
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Plus(int cartId)
        {
            var cart = await _db.ShoppingCart.FirstOrDefaultAsync(c => c.Id == cartId);
            cart.Count += 1;
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        public async Task<IActionResult> Minus(int cartId)
        {
            var cart = await _db.ShoppingCart.FirstOrDefaultAsync(c => c.Id == cartId);
            if(cart.Count==1)
            {
                _db.ShoppingCart.Remove(cart);
                await _db.SaveChangesAsync();
                var cnt = 0;
                await _db.ShoppingCart.Where(u => u.ApplicationUserId == cart.ApplicationUserId).ForEachAsync(e => { cnt = e.Count + cnt; });
                HttpContext.Session.SetInt32(SD.ssCartCount, cnt);
            }
            else
            {
                cart.Count -= 1;
                await _db.SaveChangesAsync();
            }
           
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Remove(int cartId)
        {
            var cart = await _db.ShoppingCart.FirstOrDefaultAsync(c => c.Id == cartId);
            _db.ShoppingCart.Remove(cart);

            await _db.SaveChangesAsync();
            var cnt = 0;
            await _db.ShoppingCart.Where(u => u.ApplicationUserId == cart.ApplicationUserId).ForEachAsync(e => { cnt = e.Count + cnt; });
            HttpContext.Session.SetInt32(SD.ssCartCount, cnt);

            return RedirectToAction(nameof(Index));
        }


        public async Task<IActionResult> Summary()
        {
            detailsCart = new OrderDetailsCart()
            {
                OrderHeader = new Models.OrderHeader()
            };
            detailsCart.OrderHeader.OrderTotal = 0;

            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);
            ApplicationUser applicationUser = await _db.ApplicationUser.Where(u => u.Id == claim.Value).FirstOrDefaultAsync();
            var cart = _db.ShoppingCart.Where(c => c.ApplicationUserId == claim.Value);

            if (cart != null)
            {
                detailsCart.listCart = cart.ToList();
            }

            foreach (var list in detailsCart.listCart)
            {
                list.MenuItem = await _db.MenuItem.FirstOrDefaultAsync(m => m.Id == list.MenuItemId);
                detailsCart.OrderHeader.OrderTotal += (list.MenuItem.Price * list.Count);
               
            }
            detailsCart.OrderHeader.OrderTotalOriginal = detailsCart.OrderHeader.OrderTotal;

            detailsCart.OrderHeader.PickUpName = applicationUser.Name;
            detailsCart.OrderHeader.PhoneNumber = applicationUser.PhoneNumber;
            detailsCart.OrderHeader.PickUpTime = DateTime.Now;

            if (HttpContext.Session.GetString(SD.ssCouponCode) != null)
            {
                detailsCart.OrderHeader.CouponCode = HttpContext.Session.GetString(SD.ssCouponCode);
                var couponFromDb = await _db.Coupon.Where(c => c.Name.ToLower() == detailsCart.OrderHeader.CouponCode.ToLower()).FirstOrDefaultAsync();

                detailsCart.OrderHeader.OrderTotal = SD.DiscountedPrice(couponFromDb, detailsCart.OrderHeader.OrderTotalOriginal);
            }
            return View(detailsCart);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("Summary")]
        public async Task<IActionResult> SummaryPost(string stripeToken)
        {
           
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);

            detailsCart.listCart = await _db.ShoppingCart.Where(s => s.ApplicationUserId == claim.Value).ToListAsync();

            detailsCart.OrderHeader.PaymentStatus = SD.PaymentStatusPending;
            detailsCart.OrderHeader.OrderDate = DateTime.Now;
            detailsCart.OrderHeader.UserId = claim.Value;
            detailsCart.OrderHeader.Status = SD.PaymentStatusPending;
            detailsCart.OrderHeader.PickUpTime = Convert.ToDateTime(detailsCart.OrderHeader.PickUpDate.ToShortDateString() + " " + detailsCart.OrderHeader.PickUpTime.ToShortTimeString());

            List<OrderDetails> orderDetailsList = new List<OrderDetails>();
            _db.OrderHeader.Add(detailsCart.OrderHeader);
            await _db.SaveChangesAsync();

            detailsCart.OrderHeader.OrderTotalOriginal = 0;

            foreach (var item in detailsCart.listCart)
            {
                item.MenuItem = await _db.MenuItem.FirstOrDefaultAsync(m => m.Id == item.MenuItemId);
                OrderDetails orderDetails = new OrderDetails()
                {
                    MenuItemId = item.MenuItemId,
                    OrderId = detailsCart.OrderHeader.Id,
                    Description = item.MenuItem.Description,
                    Name = item.MenuItem.Name,
                    Price = item.MenuItem.Price,
                    Count = item.Count

                };

                detailsCart.OrderHeader.OrderTotalOriginal += orderDetails.Count * orderDetails.Price;
                _db.OrderDetails.Add(orderDetails);
            }

            if (HttpContext.Session.GetString(SD.ssCouponCode) != null)
            {
                detailsCart.OrderHeader.CouponCode = HttpContext.Session.GetString(SD.ssCouponCode);
                var couponFromDb = await _db.Coupon.Where(c => c.Name.ToLower() == detailsCart.OrderHeader.CouponCode.ToLower()).FirstOrDefaultAsync();

                detailsCart.OrderHeader.OrderTotal = SD.DiscountedPrice(couponFromDb, detailsCart.OrderHeader.OrderTotalOriginal);
            }
            else
            {
                detailsCart.OrderHeader.OrderTotal = detailsCart.OrderHeader.OrderTotalOriginal;
            }
            detailsCart.OrderHeader.CouponCodeDiscount = detailsCart.OrderHeader.OrderTotalOriginal - detailsCart.OrderHeader.OrderTotal;
            
            _db.ShoppingCart.RemoveRange(detailsCart.listCart);
            HttpContext.Session.Remove(SD.ssCartCount);
            HttpContext.Session.Remove(SD.ssCouponCode);
            await _db.SaveChangesAsync();

            var options = new ChargeCreateOptions
            {
                Shipping=new ChargeShippingOptions
                {

                    Address=new AddressOptions
                    {
                        Line1 = "test address1",
                        Line2 = "test address2",
                        City = "Hyderabad",
                        PostalCode = "500016",
                        Country = "India",
                        State = "Telengana"
                    },
                    Name="Test User",
                    Phone="1234567899"
                },
               
                Amount = Convert.ToInt32(detailsCart.OrderHeader.OrderTotal * 100),
                Currency = "usd",
                Description = "Order Id: " + detailsCart.OrderHeader.Id,
                Source = stripeToken
            };
            var service = new ChargeService();
            Charge charge = service.Create(options);

            if (charge.BalanceTransactionId == null)
            {
                detailsCart.OrderHeader.Status = SD.PaymentStatusRejected;
            }
            else
            {
                detailsCart.OrderHeader.Status = charge.BalanceTransactionId;
            }

            if (charge.Status.ToLower() == "succeeded")
            {
                //email
                await _emailSender.SendEmailAsync(_db.ApplicationUser.Where(u=>u.Id==claim.Value).FirstOrDefault().Email, "Spice - Order Submitted:  " + detailsCart.OrderHeader.Id, "Order has been submitted successfully.");
                detailsCart.OrderHeader.Status = SD.PaymentStatusApproved;
                detailsCart.OrderHeader.Status = SD.StatusSubmitted;
            }
            else
            {
                detailsCart.OrderHeader.Status = SD.PaymentStatusRejected;
            }
            await _db.SaveChangesAsync();
            //return RedirectToAction("Index", "Home");
            return RedirectToAction("Confirm", "Order", new { id = detailsCart.OrderHeader.Id });
        }

    }
}