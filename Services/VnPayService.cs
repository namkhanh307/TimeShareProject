﻿using TimeShareProject.Models;
using TimeShareProject.Services;
using TimeShareWebProject.Models;

namespace TimeShareWebProject.Services
{
    public class VnPayService : IVnPayService

    {
        private readonly IConfiguration _config;
        public VnPayService(IConfiguration config)
        {
            _config = config;
        }

        public string CreatePaymentUrl(HttpContext context, Reservation model)
        {
            var tick = DateTime.Now.ToString("yyyyMMddHHmmss");

            var vnpay = new VnPayLibrary();
            vnpay.AddRequestData("vnp_Version", _config["VnPay:Version"]);
            vnpay.AddRequestData("vnp_Command", _config["VnPay:Command"]);
            vnpay.AddRequestData("vnp_TmnCode", _config["VnPay:TmnCode"]);
            vnpay.AddRequestData("vnp_Amount", (15000000000).ToString()); //Số tiền thanh toán. Số tiền không mang các ký tự phân tách thập phân, phần nghìn, ký tự tiền tệ. Để gửi số tiền thanh toán là 100,000 VND (một trăm nghìn VNĐ) thì merchant cần nhân thêm 100 lần (khử phần thập phân), sau đó gửi sang VNPAY là: 10000000

            vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
            vnpay.AddRequestData("vnp_CurrCode", _config["VnPay:CurrCode"]);
            vnpay.AddRequestData("vnp_IpAddr", Utils.GetIpAddress(context));
            vnpay.AddRequestData("vnp_Locale", _config["VnPay:Locale"]);
            vnpay.AddRequestData("vnp_OrderInfo", "Pay for " + model.UserId + model.BlockId);
            vnpay.AddRequestData("vnp_OrderType", "Other"); //default value: other
            vnpay.AddRequestData("vnp_ReturnUrl", _config["VnPay:PaymentBackReturnUrl"]);
            vnpay.AddRequestData("vnp_TxnRef", model.BlockId + model.PropertyId + tick); // Mã tham chiếu của giao dịch tại hệ thống của merchant. Mã này là duy nhất dùng để phân biệt các đơn hàng gửi sang VNPAY. Không được trùng lặp trong ngày
            vnpay.AddRequestData("reservationId", Convert.ToString(model.Id));


            var paymentUrl = vnpay.CreateRequestUrl(_config["VnPay:BaseUrl"], _config["VnPay:HashSecret"]);
            return paymentUrl;
        }

        public Transaction PaymentExecute(IQueryCollection collections)
        {
            var vnpay = new VnPayLibrary();
            foreach (var (key, value) in collections)
            {
                if (!string.IsNullOrEmpty(key) && key.StartsWith("vnp_"))
                {
                    vnpay.AddResponseData(key, value.ToString());
                }
            }
            var vnp_orderId = Convert.ToInt64(vnpay.GetResponseData("vnp_TxnRef"));
            var vnp_TransactionId = Convert.ToInt64(vnpay.GetResponseData("vnp_TransactionNo"));
            var vnp_SecureHash = collections.FirstOrDefault(p => p.Key == "vnp_SecureHash").Value;
            var vnp_ResponseCode = vnpay.GetResponseData("vnp_ResponseCode");
            var vnp_OrderInfo = vnpay.GetResponseData("vnp_OrderInfo");
            bool checkSignature = vnpay.ValidateSignature(vnp_SecureHash, _config["VnPay:HashSecret"]);
            var reservationId = Convert.ToInt16(vnpay.GetResponseData("reservationId"));
            var amount = Convert.ToDouble(vnpay.GetResponseData("vnp_Amount"));
            if (!checkSignature)
            {
                return new Transaction
                {
                    TransactionCode = Convert.ToString(vnp_orderId)
                };
            }
            return new Transaction {
                Date = DateTime.Now,
                Amount = amount,
                Status = true,
                TransactionCode = Convert.ToString(vnp_orderId),
                ReservationId = reservationId,
                Type = -1
            };
        }

        //VnPaymentRequestModel IVnPayService.PaymentExecute(IQueryCollection collections)
        //{
        //    throw new NotImplementedException();
        //}
    }
}
