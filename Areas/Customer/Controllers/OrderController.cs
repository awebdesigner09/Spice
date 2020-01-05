using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Spice.Data;
using Spice.Models;
using Spice.Models.ViewModels;
using Spice.Utility;

namespace Spice.Areas.Customer.Controllers
{
    [Area("Customer")]
    public class OrderController : Controller
    {
        private readonly ApplicationDbContext _db;
        private int PageSize = 2;
        private readonly IEmailSender _emailSender;
        public OrderController(ApplicationDbContext db,IEmailSender emailSender)
        {
            _db = db;
            _emailSender = emailSender;
        }

        [Authorize]
        public async Task<IActionResult> Confirm(int id)
        {
            var claimsIdentiy =(ClaimsIdentity) User.Identity;
            var claim = claimsIdentiy.FindFirst(ClaimTypes.NameIdentifier);
            OrderDetailsViewModel orderDetailsViewModel = new OrderDetailsViewModel()
            {
                OrderHeader = await _db.OrderHeader.Include(o => o.ApplicationUser).FirstOrDefaultAsync(o => o.Id == id && o.UserId == claim.Value),
                OrderDetails = await _db.OrderDetails.Where(o => o.OrderId == id).ToListAsync()
            };

            return View(orderDetailsViewModel);
        }


        [Authorize]
        public async Task<IActionResult> OrderHistory(int productPage=1)
        {

            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);
            OrderListViewModel orderListVM = new OrderListViewModel()
            {
                Orders = new List<OrderDetailsViewModel>()
            };

           
            List<OrderHeader> OrderHeaderList = await _db.OrderHeader.Include(o => o.ApplicationUser).Where(u => u.UserId == claim.Value).ToListAsync();
            
            foreach (OrderHeader item in OrderHeaderList)
            {
                OrderDetailsViewModel individual = new OrderDetailsViewModel
                {
                    OrderHeader = item,
                    OrderDetails = await _db.OrderDetails.Where(o => o.OrderId == item.Id).ToListAsync()
                };
                orderListVM.Orders.Add(individual);
            }

            var count = orderListVM.Orders.Count;
            orderListVM.Orders = orderListVM.Orders.OrderByDescending(p => p.OrderHeader.Id)
                .Skip((productPage - 1) * PageSize)
                .Take(PageSize).ToList();

            orderListVM.PagingInfo = new PagingInfo
            {
                CurrentPage = productPage,
                ItemsPerPage = PageSize,
                TotalItem = count,
                urlParam = "/Customer/Order/OrderHistory?productPage=:"

            };


            return View(orderListVM);


        }


        [Authorize(Roles =SD.KitchenUser+","+SD.ManagerUser)]
        public async Task<IActionResult> ManageOrder()
        {
            List<OrderDetailsViewModel> orderDetailsVM = new List<OrderDetailsViewModel>();

            List<OrderHeader> OrderHeaderList = await _db.OrderHeader.Where(o=>o.Status==SD.StatusSubmitted||o.Status==SD.StatusInProcess)
                .OrderByDescending(o=>o.PickUpTime)
                .ToListAsync();

            foreach (OrderHeader item in OrderHeaderList)
            {
                OrderDetailsViewModel individual = new OrderDetailsViewModel
                {
                    OrderHeader = item,
                    OrderDetails = await _db.OrderDetails.Where(o => o.OrderId == item.Id).ToListAsync()
                };
                orderDetailsVM.Add(individual);
            }

            return View(orderDetailsVM.OrderBy(o=>o.OrderHeader.PickUpTime));


        }

        public async Task<IActionResult> GetOrderDetails(int id)
        {
            OrderDetailsViewModel orderDetailsViewModel = new OrderDetailsViewModel()
            {
                OrderHeader = await _db.OrderHeader.FirstOrDefaultAsync(m => m.Id == id),
                OrderDetails = await _db.OrderDetails.Where(o => o.OrderId == id).ToListAsync()
            };
            orderDetailsViewModel.OrderHeader.ApplicationUser = await _db.ApplicationUser.FirstOrDefaultAsync(u => u.Id == orderDetailsViewModel.OrderHeader.UserId);

            return PartialView("_IndividualOrderDetails", orderDetailsViewModel);
            
        }

        public IActionResult GetOrderStatus(string orderStatus)
        {
            var image="";
            switch (orderStatus)
            {
                case SD.StatusSubmitted:
                    image = SD.ImageStatusSubmitted;break;
                case SD.StatusInProcess:
                    image = SD.ImageStatusInProcess;break;
                case SD.StatusReady:
                    image = SD.ImageStatusReady;break;
                case SD.StatusCompleted:
                    image = SD.ImageStatusCompleted;break;
                default:
                    image= SD.ImageStatusSubmitted; break;
            }
           
            return PartialView("_OrderStatusPartial", image );
        }

        [Authorize(Roles =SD.KitchenUser+","+SD.ManagerUser)]
        public async Task<IActionResult> OrderPrepare(int OrderId)
        {
            OrderHeader orderHeader = await _db.OrderHeader.FindAsync(OrderId);
            orderHeader.Status = SD.StatusInProcess;
            await _db.SaveChangesAsync();
            return RedirectToAction("ManageOrder", "Order");
        }

        [Authorize(Roles = SD.KitchenUser + "," + SD.ManagerUser)]
        public async Task<IActionResult> OrderReady(int OrderId)
        {
            OrderHeader orderHeader = await _db.OrderHeader.FindAsync(OrderId);
            orderHeader.Status = SD.StatusReady;
            await _db.SaveChangesAsync();

            //Notify user by email to pickup order
            await _emailSender.SendEmailAsync(_db.ApplicationUser.Where(u => u.Id == orderHeader.UserId).FirstOrDefault().Email, "Spice - Order Ready:  " + orderHeader.Id, "Order is ready for pickup.");

            return RedirectToAction("ManageOrder", "Order");
        }

        [Authorize(Roles = SD.KitchenUser + "," + SD.ManagerUser)]
        public async Task<IActionResult> OrderCancel(int OrderId)
        {
            OrderHeader orderHeader = await _db.OrderHeader.FindAsync(OrderId);
            orderHeader.Status = SD.StatusCancelled;
            await _db.SaveChangesAsync();
            await _emailSender.SendEmailAsync(_db.ApplicationUser.Where(u => u.Id == orderHeader.UserId).FirstOrDefault().Email, "Spice - Order Cancelled:  " + orderHeader.Id, "Order has been Cancelled successfully.");

            return RedirectToAction("ManageOrder", "Order");
        }

        [Authorize]
        public async Task<IActionResult> OrderPickup(int productPage = 1,string searchEmail=null, string searchPhone = null, string searchName = null)
        {

            //var claimsIdentity = (ClaimsIdentity)User.Identity;
            //var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);
            OrderListViewModel orderListVM = new OrderListViewModel()
            {
                Orders = new List<OrderDetailsViewModel>()
            };
            StringBuilder param = new StringBuilder();
            param.Append("/Customer/Order/OrderPickup?productPage=:");
            param.Append("&searchName=");
            if(searchName!=null)
            {
                param.Append(searchName);
            }
            param.Append("&searchEmail=");
            if (searchEmail != null)
            {
                param.Append(searchEmail);
            }
            param.Append("&searchPhone=");
            if (searchPhone != null)
            {
                param.Append(searchPhone);
            }
            ApplicationUser user = new ApplicationUser();
            List<OrderHeader> OrderHeaderList = new List<OrderHeader>();
            if (searchName != null || searchEmail != null || searchPhone != null)
            {

                if (searchName != null)
                {
                    OrderHeaderList = await _db.OrderHeader.Include(o => o.ApplicationUser)
                        .Where(u => u.PickUpName.ToLower().Contains(searchName.ToLower()))
                        .OrderByDescending(o => o.OrderDate).ToListAsync();
                }
                else
                {
                    if (searchEmail != null)
                    {
                        user = await _db.ApplicationUser.Where(u => u.Email.ToLower().Contains(searchEmail.ToLower())).FirstOrDefaultAsync();

                        OrderHeaderList = await _db.OrderHeader.Include(o => o.ApplicationUser)
                            .Where(o => o.UserId == user.Id)
                            .OrderByDescending(o => o.OrderDate).ToListAsync();
                    }
                    else
                    {
                        if (searchPhone != null)
                        {
                            OrderHeaderList = await _db.OrderHeader.Include(o => o.ApplicationUser)
                                .Where(u => u.PhoneNumber.Contains(searchPhone))
                                .OrderByDescending(o => o.OrderDate).ToListAsync();
                        }
                    }
                }

            }
            else
            {

                OrderHeaderList = await _db.OrderHeader.Include(o => o.ApplicationUser).Where(o => o.Status == SD.StatusReady).ToListAsync();
            }
                foreach (OrderHeader item in OrderHeaderList)
                {
                    OrderDetailsViewModel individual = new OrderDetailsViewModel
                    {
                        OrderHeader = item,
                        OrderDetails = await _db.OrderDetails.Where(o => o.OrderId == item.Id).ToListAsync()
                    };
                    orderListVM.Orders.Add(individual);
                }
            
            var count = orderListVM.Orders.Count;
            orderListVM.Orders = orderListVM.Orders.OrderByDescending(p => p.OrderHeader.Id)
                .Skip((productPage - 1) * PageSize)
                .Take(PageSize).ToList();

            orderListVM.PagingInfo = new PagingInfo
            {
                CurrentPage = productPage,
                ItemsPerPage = PageSize,
                TotalItem = count,
                urlParam = param.ToString()

            };


            return View(orderListVM);


        }

        [Authorize(Roles = SD.FrontDeskUser + "," + SD.ManagerUser)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("OrderPickup")]
        public async Task<IActionResult> OrderPickupPost(int orderId)
        {
            OrderHeader orderHeader = await _db.OrderHeader.FindAsync(orderId);
            orderHeader.Status = SD.StatusCompleted;
            await _db.SaveChangesAsync();
            await _emailSender.SendEmailAsync(_db.ApplicationUser.Where(u => u.Id == orderHeader.UserId).FirstOrDefault().Email, "Spice - Order Complete:  " + orderHeader.Id, "Order has been completed successfully.");

            return RedirectToAction("OrderPickup", "Order");
        }


    }
}